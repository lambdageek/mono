/**
 * \file 
 * Thread barrier implementation
 *
 * Copyright 2020 Microsoft
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 */

#ifndef __MONO_UTILS_BARRIER_H__
#define __MONO_UTILS_BARRIER_H__

#include <glib.h>

#include "mono/utils/mono-publib.h"
#include "mono/utils/mono-compiler.h"
#include "mono/utils/atomic.h"
#include "mono/utils/os-event.h"

typedef struct _MonoCoopBarrier MonoCoopBarrier;

typedef void (*MonoCoopBarrierCallback) (gpointer user_data);

/* We use our own barrier instead of a POSIX barrier, for example in order to:
 * 1. have coop-aware waits.
 * 2. because we want to add and remove participants from the barrier after it is initialized.
 */
struct _MonoCoopBarrier {
	volatile int32_t current_total_count; /* bitfields */
	volatile int64_t current_phase;
	MonoOSEvent even_event;
	MonoOSEvent odd_event;
	MonoCoopBarrierCallback callback;
	gpointer user_data;
};

#define MONO_BARRIER_TOTAL_MASK 0x7fff
#define MONO_BARRIER_CURRENT_MASK 0x7fff0000
#define MONO_BARRIER_CURRENT_SHIFT 16
#define MONO_BARRIER_PHASE_SENSE_MASK 0x80000000

static inline void
mono_coop_barrier_unpack_counts (int32_t current_total_count, int *current, int *total, gboolean *phase_sense)
{
	*current = (current_total_count & MONO_BARRIER_CURRENT_MASK) >> MONO_BARRIER_CURRENT_SHIFT;
	*total = (current_total_count & MONO_BARRIER_TOTAL_MASK);
	*phase_sense = !(current_total_count & MONO_BARRIER_PHASE_SENSE_MASK);
}

void
mono_coop_barrier_init (MonoCoopBarrier *barrier, int total_participants, MonoCoopBarrierCallback callback, gpointer user_data);

void
mono_coop_barrier_destroy (MonoCoopBarrier *barrier);

int64_t
mono_coop_barrier_add_participants (MonoCoopBarrier *barrier, int num_participants);

void
mono_coop_barrier_remove_participants (MonoCoopBarrier *barrier, int num_participants);

void
mono_coop_barrier_signal_and_wait (MonoCoopBarrier *barrier);

gboolean
mono_coop_barrier_signal_and_timedwait (MonoCoopBarrier *barrier, uint32_t timeout_ms);

static inline int
mono_coop_barrier_participant_count (MonoCoopBarrier *barrier)
{
	int current;
	int total;
	gboolean phase_sense;
	mono_coop_barrier_unpack_counts (barrier->current_total_count, &current, &total, &phase_sense);
	return total;
}

static inline int
mono_coop_barrier_participants_remaining (MonoCoopBarrier *barrier)
{
	int current;
	int total;
	gboolean phase_sense;
	mono_coop_barrier_unpack_counts (barrier->current_total_count, &current, &total, &phase_sense);
	return total - current;
}

static inline int64_t
mono_coop_barrier_current_phase (MonoCoopBarrier *barrier)
{
	return mono_atomic_load_i64 (&barrier->current_phase);
}



#endif /*__MONO_UTILS_BARRIER_H__*/
