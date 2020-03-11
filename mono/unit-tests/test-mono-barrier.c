/*
 * test-mono-barrier.c: Unit tests for MonoCoopBarrier.
 *
 * Copyright 2020 Microsoft.
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 */
#include "config.h"

#include "utils/mono-threads.h"
#include "utils/checked-build.h"
#include "utils/mono-barrier.h"
#include "metadata/w32handle.h"

#include <stdint.h>
#include <stdlib.h>
#include <pthread.h>

#define CHECK(fail_count,expr) do {					\
		if (!(expr)) {						\
			fprintf (stderr, "%s:%d check '%s' failed\n", __FILE__, __LINE__, #expr); \
			fail_count++;					\
		}							\
	} while (0)

static void
incr_cb (gpointer user_data)
{
	int* p = (int*)user_data;
	*p += 1;
}


static int
one_thread_barrier (void)
{
	int fail_count = 0;

	MonoCoopBarrier bar;
	int callback_called = 0;
	mono_coop_barrier_init (&bar, 1, incr_cb, &callback_called);

	CHECK (fail_count, mono_coop_barrier_participants_remaining (&bar) == 1);
	CHECK (fail_count, mono_coop_barrier_current_phase (&bar) == 0);

	mono_coop_barrier_signal_and_wait (&bar);

	CHECK (fail_count, mono_coop_barrier_current_phase (&bar) == 1);

	mono_coop_barrier_destroy (&bar);

	CHECK (fail_count, callback_called == 1);

	return fail_count;
}

typedef struct simple_worker_data {
	int iters;
	MonoCoopBarrier *bar;
	volatile int32_t *counter;
} simple_worker_data;

static gpointer
simple_worker (gpointer user_data)
{
	simple_worker_data *p = (simple_worker_data*)user_data;

	mono_thread_info_attach ();

	int iters = p->iters;
	for (int i = 0; i < iters; ++i) {
		mono_atomic_inc_i32 (p->counter);
		mono_coop_barrier_signal_and_wait (p->bar);
	}

	return NULL;
}
       

static int
two_thread_barrier (void)
{
	int fail_count = 0;

	int callback_called = 0;
	MonoCoopBarrier bar;
	mono_coop_barrier_init (&bar, 2, incr_cb, &callback_called);
	
	CHECK (fail_count, mono_coop_barrier_participants_remaining (&bar) == 2);
	CHECK (fail_count, mono_coop_barrier_current_phase (&bar) == 0);

	volatile int32_t signal_counter = 0;

	simple_worker_data data;
	/* doing enough iterations to flip between even, odd and even phase again */
	data.iters = 3;
	data.bar = &bar;
	data.counter = &signal_counter;

	pthread_t t;
	pthread_create (&t, NULL, simple_worker, &data);

	CHECK (fail_count, signal_counter >= 0 && signal_counter <= 1);

	mono_coop_barrier_signal_and_wait (&bar);

	CHECK (fail_count, callback_called == 1);
	CHECK (fail_count, signal_counter >= 1 && signal_counter <= 2);
	CHECK (fail_count, mono_coop_barrier_current_phase (&bar) == 1);

	mono_coop_barrier_signal_and_wait (&bar);

	CHECK (fail_count, callback_called == 2);
	CHECK (fail_count, signal_counter >= 2 && signal_counter <= 3);
	CHECK (fail_count, mono_coop_barrier_current_phase (&bar) == 2);

	mono_coop_barrier_signal_and_wait (&bar);

	CHECK (fail_count, callback_called == 3);
	CHECK (fail_count, signal_counter == 3);
	CHECK (fail_count, mono_coop_barrier_current_phase (&bar) == 3);

	pthread_join (t, NULL);

	mono_coop_barrier_destroy (&bar);

	return fail_count;
}

static int
three_thread_barrier_with_remove (void)
{
	int fail_count = 0;

	MonoCoopBarrier bar;

	mono_coop_barrier_init (&bar, 3, NULL, NULL);

	volatile int32_t signal_counter = 0;
	simple_worker_data data;
	data.iters = 3;
	data.bar = &bar;
	data.counter = &signal_counter;

	pthread_t t1, t2;
	pthread_create (&t1, NULL, simple_worker, &data);
	pthread_create (&t2, NULL, simple_worker, &data);

	CHECK (fail_count, signal_counter >= 0 && signal_counter <= 2);

	mono_coop_barrier_signal_and_wait (&bar);

	CHECK (fail_count, signal_counter >= 2 && signal_counter <= 4);

	mono_coop_barrier_remove_participants (&bar, 1);

	// the remaining threads synchronize amongs themselves and exit the loop

	pthread_join (t1, NULL);
	pthread_join (t2, NULL);

	CHECK (fail_count, signal_counter == 6);
	CHECK (fail_count, mono_coop_barrier_current_phase (&bar) == 3);
	
	mono_coop_barrier_destroy (&bar);

	return fail_count;
}

static void
monotest_thread_state_init (MonoThreadUnwindState *ctx)
{
}

#ifdef __cplusplus
extern "C"
#endif
int
test_mono_barrier_main (void);

#define monotest_setup_async_callback          NULL
#define monotest_thread_state_init_from_sigctx NULL
#define monotest_thread_state_init_from_handle NULL

int
test_mono_barrier_main (void)
{
	static const MonoThreadInfoRuntimeCallbacks ticallbacks = {
		MONO_THREAD_INFO_RUNTIME_CALLBACKS (MONO_INIT_CALLBACK, monotest)
	};

	int res = 0;

	CHECKED_MONO_INIT ();
	mono_thread_info_init (sizeof (MonoThreadInfo));
	mono_thread_info_runtime_init (&ticallbacks);
#ifndef HOST_WIN32
	mono_w32handle_init ();
#endif

	mono_thread_info_attach ();

	res += one_thread_barrier ();
	res += two_thread_barrier ();
	res += three_thread_barrier_with_remove ();

	return res;
}
