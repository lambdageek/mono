/**
 * \file 
 * Thread barrier implementation
 *
 * Copyright 2020 Microsoft
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 */

/* Implementation is copied largely from reference source System.Threading.Barrier */

#include <config.h>

#include <glib.h>

#include "mono-barrier.h"
#include "mono/utils/atomic.h"
#include "mono/utils/os-event.h"
#include "mono/utils/mono-threads-api.h"

static gboolean
event_is_set (MonoOSEvent *evt)
{
	return evt->signalled;
}

static void
spin (void)
{
	/* FIXME: spin. also maybe need a safepoint */
}

/* Max number of participants is the same as the mask for total participants. */
#define MAX_PARTICIPANTS MONO_BARRIER_TOTAL_MASK

/**
 *
 * Implementation notes:
 *
 * phase_sense == TRUE means current_phase is an even number, and participants will wait for even_event.
 * Otherwise the current_phase is odd and participants will wait for the odd_event.
 *
 * When the current phase is completed:
 *
 * - The first thing that happens is the phase sense is negated and the current count is set to 0;
 * - then the callback runs;
 * - then the current_phase counter is incremented;
 * - then the event for the opposite phase is reset;
 * - then the event for the current phase is set (which unblocks the participants)
 *
 * Because these steps happen non-atomically, the code for adding/removing
 * participants, and for signalling and waiting has to figure out what step
 * it's in, which is the main source of complications.
 *
 */


static int32_t
barrier_pack_counts (int current, int total, gboolean phase_sense)
{
	int32_t current_total = 0;
	current_total |= (((int32_t)current) & MAX_PARTICIPANTS) << MONO_BARRIER_CURRENT_SHIFT;
	current_total |= ((int32_t)total) & MAX_PARTICIPANTS;
	current_total |= phase_sense ? 0 : MONO_BARRIER_PHASE_SENSE_MASK;
	return current_total;
}

static gboolean
barrier_atomic_set_current_total (MonoCoopBarrier *barrier, int32_t old_current_total_count, int current, int total, gboolean phase_sense)
{
	int32_t new_current_total_count = barrier_pack_counts (current, total, phase_sense);
	return mono_atomic_cas_i32 (&barrier->current_total_count, new_current_total_count, old_current_total_count) == old_current_total_count;
}

static int64_t
barrier_get_phase (MonoCoopBarrier *barrier)
{
	return mono_atomic_load_i64 (&barrier->current_phase);
}

static void
barrier_incr_phase (MonoCoopBarrier *barrier)
{
	mono_atomic_inc_i64 (&barrier->current_phase);
}

static void
set_reset_events (MonoCoopBarrier *barrier, gboolean observed_sense);

static void
finish_phase (MonoCoopBarrier *barrier, gboolean observed_sense);

static gboolean
discontinuous_wait (MonoCoopBarrier *barrier, MonoOSEvent *current_event, uint32_t timeout_ms, int64_t phase);

static void
wait_for_current_phase (MonoCoopBarrier *barrier, MonoOSEvent *current_event, int64_t phase);



void
mono_coop_barrier_init (MonoCoopBarrier *barrier, int total_participants, MonoCoopBarrierCallback callback, gpointer user_data)
{
	g_assert (barrier);
	memset (barrier, 0, sizeof (MonoCoopBarrier));
	barrier->current_total_count = barrier_pack_counts (0, total_participants, TRUE);
	barrier->current_phase = 0;
	mono_os_event_init (&barrier->even_event, FALSE);
	mono_os_event_init (&barrier->odd_event, TRUE);
	barrier->callback = callback;
	barrier->user_data = user_data;

}

void
mono_coop_barrier_destroy (MonoCoopBarrier *barrier)
{
	g_assert (barrier);
	int total;
	int current;
	gboolean sense;
	mono_coop_barrier_unpack_counts (barrier->current_total_count, &current, &total, &sense);
	/* FIXME: handle blocked participants here? */
	g_assert (current == 0);
	mono_os_event_destroy (&barrier->even_event);
	mono_os_event_destroy (&barrier->odd_event);
}

int64_t
mono_coop_barrier_add_participants (MonoCoopBarrier *barrier, int num_participants)
{
	g_assert (barrier);

	int64_t new_phase = 0;
	
	while (1) {
		int32_t current_total = barrier->current_total_count;
		int total;
		int current;
		gboolean sense;
		mono_coop_barrier_unpack_counts (current_total, &current, &total, &sense);
		g_assert (num_participants + total <= MAX_PARTICIPANTS);

		if (barrier_atomic_set_current_total (barrier, current_total, current, num_participants + total, sense)) {
			
			// Figure out if the new participants will join the current phase or the next one.  if the phase
			// number doesn't match the phase sense, it means the sense was flipped (and the phase was
			// finished) but the callback action hasn't finished yet
			int64_t current_phase = barrier_get_phase (barrier);
			new_phase = (sense != (current_phase % 2 == 0)) ? current_phase + 1 : current_phase;

			// if the new participants will join the next phase, wait for the callback for the current phase
			// to finish and for the event to be reset (and the opposite event to be set)
			if (new_phase != current_phase) {
				// wait for the opposite event
				MONO_ENTER_GC_SAFE;
				mono_os_event_wait_one (sense ? &barrier->odd_event : &barrier->even_event, MONO_INFINITE_WAIT, TRUE);
				MONO_EXIT_GC_SAFE;
			} else {
				//This else to fix the racing where the current phase has been finished, m_currentPhase
				// has been updated but the events have not been set/reset yet otherwise when this
				// participant calls SignalAndWait it will wait on a set event however all other
				// participants have not arrived yet.
				if (sense && event_is_set (&barrier->even_event))
					mono_os_event_reset (&barrier->even_event);
				else if (!sense && event_is_set (&barrier->odd_event))
					mono_os_event_reset (&barrier->odd_event);
			}
			break;
		}
		spin ();
	}

	return new_phase;
}

void
mono_coop_barrier_remove_participants (MonoCoopBarrier *barrier, int num_participants)
{
	g_assert (barrier);

	while (1) {
		int32_t current_total = barrier->current_total_count;
		int current;
		int total;
		int sense;
		mono_coop_barrier_unpack_counts (current_total, &current, &total, &sense);

		g_assert (total >= num_participants);

		g_assert (total - num_participants >= current);

		int remaining = total - num_participants;

		if (remaining > 0 && current == remaining)
		{
			// All the remaining participants have already reached the barrier. Finish the current phase
			if (barrier_atomic_set_current_total (barrier, current_total, 0, remaining, !sense))
			{
				finish_phase (barrier, sense);
				break;
			}
		} else {
			if (barrier_atomic_set_current_total (barrier, current_total, current, remaining, sense))
				break;
		}
	}
}

void
mono_coop_barrier_signal_and_wait (MonoCoopBarrier *barrier)
{
	mono_coop_barrier_signal_and_timedwait (barrier, MONO_INFINITE_WAIT);
}

gboolean
mono_coop_barrier_signal_and_timedwait (MonoCoopBarrier *barrier, uint32_t timeout_ms)
{
	g_assert (barrier);
	
	int32_t current_total;
	int current;
	int total;
	gboolean sense_before;
	int64_t phase;

	// Try to add ourselves to the count of participants that reached the barrier
	
	while (1) {
		current_total = barrier->current_total_count;
		mono_coop_barrier_unpack_counts (current_total, &current, &total, &sense_before);
		phase = barrier_get_phase (barrier);

		g_assert (total > 0);

                // Try to detect if the number of threads for this phase exceeded the total number of participants or
                // not. This can be detected if the current is zero which means all participants for that phase has
                // arrived and the phase number is not changed yet
		if (current == 0 && sense_before != (phase % 2 == 0)) {
			g_error ("barrier %p (current_total = %lx) current count is 0, but phase %lu doesn't match expected sense.  Too many threads reached the barrier - expected %d.", (void*)barrier, (unsigned long)current_total, (unsigned long)phase, (int)total);
		}

		if (current + 1 == total) {
			// This was the last thread. Finish the phase.

			if (barrier_atomic_set_current_total (barrier, current_total, 0, total, !sense_before)) {
				finish_phase (barrier, sense_before);
				return TRUE;
			}
		} else if (barrier_atomic_set_current_total (barrier, current_total, current + 1, total, sense_before))
			break;

		spin ();
	}

	// If we get here, not every participant arrived yet and we must wait

	MonoOSEvent *evt = sense_before ? &barrier->even_event : &barrier->odd_event;

	gboolean wait_result = discontinuous_wait (barrier, evt, timeout_ms, phase);

	if (!wait_result) {
		// timed out, try to roll back the number of participants that reached the barrier
		while (1) {
			gboolean new_sense;
			current_total = barrier->current_total_count;
			mono_coop_barrier_unpack_counts (current_total, &current, &total, &new_sense);

			// If the timeout expired and the phase has just finished, return true and this is considered as
			//succeeded SignalAndWait otherwise the timeout expired and the current phase has not been
			//finished yet, return false The phase is finished if the phase member variable is changed
			//(incremented) or the sense has been changed we have to use the statements in the comparison
			//below for two cases:
			// 1- The sense is changed but the last thread didn't update the phase yet
			// 2- The phase is already incremented but the sense flipped twice due to the termination of the next phase
			if (phase < barrier_get_phase (barrier) || sense_before != new_sense) {
				// The current phase has been finished, but we shouldn't return before the events are
				// set/reset otherwise this thread could start next phase and the appropriate event has
				// not reset yet which could make it return immediately from the next phase
				// SignalAndWait before waiting other threads
				wait_for_current_phase (barrier, evt, phase);
				g_assert (phase < barrier_get_phase (barrier));
				break;
			}

			if (barrier_atomic_set_current_total (barrier, current_total, current - 1, total, sense_before)) {
				return FALSE;
			}

			spin ();
		}
	}

	/* reached the next phase */
	return TRUE;
}

void
finish_phase (MonoCoopBarrier *barrier, gboolean observed_sense)
{
	if (barrier->callback) {
		barrier->callback (barrier->user_data);
	}
	set_reset_events (barrier, observed_sense);
}

void
set_reset_events (MonoCoopBarrier *barrier, gboolean observed_sense)
{
	barrier_incr_phase (barrier);
	if (observed_sense) {
		mono_os_event_reset (&barrier->odd_event);
		mono_os_event_set (&barrier->even_event);
	} else {
		mono_os_event_reset (&barrier->even_event);
		mono_os_event_set (&barrier->odd_event);
	}
}

void
wait_for_current_phase (MonoCoopBarrier *barrier, MonoOSEvent *current_event, int64_t observed_phase)
{
	//spin until either of these two conditions succeeds
	//1- The event is set
	//2- the phase count is incremented more than one time, this means the next phase is finished as well,
	//but the event will be reset again, so we check the phase count instead
	while (!event_is_set (current_event) && barrier_get_phase (barrier) - observed_phase <= 1) {
		spin ();
	}
}

gboolean
discontinuous_wait (MonoCoopBarrier *barrier, MonoOSEvent *current_event, uint32_t timeout_ms, int64_t observed_phase)
{
        /// The reason of discontinuous waiting instead of direct waiting on the event is to avoid the race where the
        /// sense is changed twice because the next phase is finished (due to either RemoveParticipant is called or
        /// another thread joined the next phase instead of the current thread) so the current thread will be stuck on
        /// the event because it is reset back
	uint32_t max_wait_ms = 100;
	uint32_t wait_ceiling = 10000; // 10 seconds
	while (observed_phase == barrier_get_phase (barrier)) {
		uint32_t wait_time = timeout_ms == MONO_INFINITE_WAIT ? max_wait_ms : (uint32_t)MIN (max_wait_ms, timeout_ms);

		int res;

		MONO_ENTER_GC_SAFE;
		res = mono_os_event_wait_one (current_event, wait_time, TRUE);
		MONO_EXIT_GC_SAFE;

		if (!res)
			return TRUE;

		if (timeout_ms != MONO_INFINITE_WAIT) {
			if (timeout_ms <= wait_time)
				return FALSE;
			timeout_ms -= wait_time;
		}

		// if we haven't hit the ceiling, double the max wait time of the next iteration, upto the ceiling.
		max_wait_ms = max_wait_ms >= wait_ceiling ? wait_ceiling : (uint32_t) MIN (max_wait_ms << 1, wait_ceiling);
	}

	//if we exited the loop because the observed phase doesn't match the current phase, then we have to spin to make sure
	//the event is set or the next phase is finished
	wait_for_current_phase (barrier, current_event, observed_phase);

	return TRUE;
}
