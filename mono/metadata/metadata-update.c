/**
 * \file
 * Routines for publishing flying orchids
 *
 * Copyright 2020 Microsoft
 *
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 */

#include <config.h>
#include <glib.h>
#include "mono/metadata/metadata-internals.h"
#include "mono/metadata/metadata-update.h"
#include "mono/metadata/object-internals.h"
#include "mono/metadata/tokentype.h"
#include "mono/utils/mono-coop-mutex.h"
#include "mono/utils/mono-logger-internals.h"
#include "mono/utils/mono-path.h"

#if 1
#define UPDATE_DEBUG(stmt) do { stmt; } while (0)
#else
#define UPDATE_DEBUG(stmt) /*empty */
#endif


/* Maps each MonoTableInfo* to the MonoImage that it belongs to.  This is
 * mapping the base image MonoTableInfos to the base MonoImage.  We don't need
 * this for deltas.
 */
static GHashTable *table_to_image;
static MonoCoopMutex table_to_image_mutex;

static void
table_to_image_lock (void)
{
	mono_coop_mutex_lock (&table_to_image_mutex);
}

static void
table_to_image_unlock (void)
{
	mono_coop_mutex_unlock (&table_to_image_mutex);
}

static void
table_to_image_init (void)
{
	mono_coop_mutex_init (&table_to_image_mutex);
	table_to_image = g_hash_table_new (NULL, NULL);
}

static gboolean
remove_base_image (gpointer key, gpointer value, gpointer user_data)
{
	MonoImage *base_image = (MonoImage*)user_data;
	MonoImage *value_image = (MonoImage*)value;
	return (value_image == base_image);
}

void
mono_metadata_update_cleanup_on_close (MonoImage *base_image)
{
	/* remove all keys that map to the given image */
	table_to_image_lock ();
	g_hash_table_foreach_remove (table_to_image, remove_base_image, (gpointer)base_image);
	table_to_image_unlock ();
}

static void
table_to_image_add (MonoImage *base_image)
{
	/* If at least one table from this image is already here, they all are */
	if (g_hash_table_contains (table_to_image, &base_image->tables[MONO_TABLE_MODULE]))
		return;
	table_to_image_lock ();
	if (g_hash_table_contains (table_to_image, &base_image->tables[MONO_TABLE_MODULE])) {
	        table_to_image_unlock ();
		return;
	}
	for (int idx = 0; idx < MONO_TABLE_NUM; ++idx) {
		MonoTableInfo *table = &base_image->tables[idx];
		g_hash_table_insert (table_to_image, table, base_image);
	}
	table_to_image_unlock ();
}

MonoImage*
mono_image_open_dmeta_from_data (MonoImage *base_image, uint32_t generation, const char *dmeta_name, gconstpointer dmeta_bytes, uint32_t dmeta_len, MonoImageOpenStatus *status);

MonoDilFile *
mono_dil_file_open (const char *dil_path);

void
mono_image_append_delta (MonoImage *base, MonoImage *delta);


void
mono_metadata_update_init (void)
{
	table_to_image_init ();
}


static
void
mono_metadata_update_invoke_hook (MonoDomain *domain, MonoAssemblyLoadContext *alc, uint32_t generation)
{
	if (mono_get_runtime_callbacks ()->metadata_update_published)
		mono_get_runtime_callbacks ()->metadata_update_published (domain, alc, generation);
}

static uint32_t update_published, update_alloc_frontier;

uint32_t
mono_metadata_update_prepare (MonoDomain *domain) {
	/* TODO: take a lock? one updater at a time seems like a good invariant.
	 * TODO: assert that the updater isn't depending on current metadata, else publishing might block.
	 */
	return ++update_alloc_frontier;
}

gboolean
mono_metadata_update_available (void) {
	return update_published < update_alloc_frontier;
}

gboolean
mono_metadata_wait_for_update (uint32_t timeout_ms)
{
	/* TODO: give threads a way to voluntarily wait for an update to be published. */
	g_assert_not_reached ();
}

void
mono_metadata_update_publish (MonoDomain *domain, MonoAssemblyLoadContext *alc, uint32_t generation) {
	g_assert (update_published < generation && generation <= update_alloc_frontier);
	/* TODO: wait for all threads that are using old metadata to update. */
	mono_metadata_update_invoke_hook (domain, alc, generation);
}

struct _MonoDilFile {
	MonoFileMap *filed;
	gpointer handle;
	char *il;
};

void
mono_image_append_delta (MonoImage *base, MonoImage *delta)
{
	/* FIXME: needs locking. Assumes one updater at a time */
	if (!base->delta_image) {
		base->delta_image = base->delta_image_last = g_slist_prepend (NULL, delta);
		return;
	}
	g_assert (((MonoImage*)base->delta_image_last->data)->generation < delta->generation);
	base->delta_image_last = g_slist_append (base->delta_image_last, delta);
}

MonoImage*
mono_image_open_dmeta_from_data (MonoImage *base_image, uint32_t generation, const char *dmeta_name, gconstpointer dmeta_bytes, uint32_t dmeta_len, MonoImageOpenStatus *status)
{
	MonoAssemblyLoadContext *alc = mono_image_get_alc (base_image);
	MonoImage *dmeta_image = mono_image_open_from_data_internal (alc, (char*)dmeta_bytes, dmeta_len, TRUE, status, FALSE, TRUE, dmeta_name, dmeta_name);

	dmeta_image->generation = generation;

	/* base_image takes ownership of 1 refcount ref of dmeta_image */
	mono_image_append_delta (base_image, dmeta_image);

	return dmeta_image;
}

MonoDilFile *
mono_dil_file_open (const char *dil_path)
{
	MonoDilFile *dil = g_new0 (MonoDilFile, 1);

	dil->filed = mono_file_map_open (dil_path);
	g_assert (dil->filed);
	dil->il = (char *) mono_file_map_fileio (
			mono_file_map_size (dil->filed),
			MONO_MMAP_READ | MONO_MMAP_PRIVATE,
			mono_file_map_fd (dil->filed),
			0,
			&dil->handle);
	return dil;
}

void
mono_dil_file_close (MonoDilFile *dil)
{
	mono_file_map_close (dil->filed);
}

void
mono_dil_file_destroy (MonoDilFile *dil)
{
	g_free (dil);
}

static void
dump_update_summary (MonoImage *image_base, MonoImage *image_dmeta, uint32_t string_heap_offset)
{
	int rows;

	mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "dmeta tables:");
	for (int idx = 0; idx < MONO_TABLE_NUM; ++idx) {
		if (image_dmeta->tables [idx].base) {
			mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "\t%x \"%s\"", idx, mono_meta_table_name (idx));
		}
	}


	rows = mono_image_get_table_rows (image_base, MONO_TABLE_METHOD);
	for (int i = 1; i <= rows ; ++i) {
		ERROR_DECL (error);
		int token = MONO_TOKEN_METHOD_DEF | i;
		MonoMethod *method = mono_get_method_checked (image_base, token, NULL, NULL, error);
		mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "base  method %d (token=0x%08x): %s", i, token, mono_method_get_name_full (method, TRUE, TRUE, MONO_TYPE_NAME_FORMAT_IL));
	}

	mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "================================");


	rows = mono_image_get_table_rows (image_base, MONO_TABLE_METHOD);
	for (int i = 1; i <= rows ; ++i) {
		guint32 cols [MONO_METHOD_SIZE];
		mono_metadata_decode_row (&image_base->tables [MONO_TABLE_METHOD], i - 1, cols, MONO_METHOD_SIZE);
		const char *name = mono_metadata_string_heap (image_base, cols [MONO_METHOD_NAME]);
		guint32 rva = cols [MONO_METHOD_RVA];
		mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "base  method i=%d, rva=%d/0x%04x, name=%s\n", i, rva, rva, name);
	}

	mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "================================");

	rows = mono_image_get_table_rows (image_dmeta, MONO_TABLE_METHOD);
	for (int i = 1; i <= rows ; ++i) {
		guint32 cols [MONO_METHOD_SIZE];
		mono_metadata_decode_row (&image_dmeta->tables [MONO_TABLE_METHOD], i - 1, cols, MONO_METHOD_SIZE);
		const char *name = mono_metadata_string_heap (image_dmeta, cols [MONO_METHOD_NAME] - string_heap_offset);
		guint32 rva = cols [MONO_METHOD_RVA];
		mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "dmeta method i=%d, rva=%d/0x%04x, name=%s", i, rva, rva, name);
	}

	mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "================================");
}

typedef struct _EncRecs {
	// for each table, the row in the EncMap table that has the first token for remapping it?
	uint32_t enc_recs [MONO_TABLE_NUM];
} EncRecs;

static void
start_encmap (MonoImage *image_dmeta, EncRecs *enc_recs)
{
	memset (enc_recs, 0, sizeof (*enc_recs));
	MonoTableInfo *encmap = &image_dmeta->tables [MONO_TABLE_ENCMAP];
	int table;
	int prev_table = -1;
	if (!encmap->rows)
		return;
	int idx;
	for (idx = 1; idx <= encmap->rows; ++idx) {
		guint32 cols[MONO_ENCMAP_SIZE];
		mono_metadata_decode_row (encmap, idx - 1, cols, MONO_ENCMAP_SIZE);
		uint32_t tok = cols [MONO_ENCMAP_TOKEN];
		table = mono_metadata_token_table (tok);
		g_assert (table >= 0 && table <= MONO_TABLE_LAST);
		g_assert (table != MONO_TABLE_ENCLOG);
		g_assert (table != MONO_TABLE_ENCMAP);
		/* FIXME: this function is cribbed from CMiniMdRW::StartENCMap
		 * https://github.com/dotnet/runtime/blob/4f9ae42d861fcb4be2fcd5d3d55d5f227d30e723/src/coreclr/src/md/enc/metamodelenc.cpp#L215,
		 * but for some reason the following assertion table >= prev_table fails.
		 */
		g_assert (table >= prev_table);
		if (table == prev_table)
			continue;
		while (prev_table < table) {
			prev_table++;
			enc_recs->enc_recs [prev_table] = idx;
		}
	}
	while (prev_table < MONO_TABLE_NUM - 1) {
		prev_table++;
		enc_recs->enc_recs [prev_table] = idx;
	}


	/* FIXME: want MONO_TABLE_NUM for the upper bound, but mono_meta_table_name is only defined upto GENERICPARAMCONSTRAINT */
	for (int i = 0 ; i <= MONO_TABLE_GENERICPARAMCONSTRAINT; ++i) {
		mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "enc_recs [%02x] / %s = 0x%02x", i, mono_meta_table_name (i), enc_recs->enc_recs[i]);
	}
}


void
mono_image_load_enc_delta (MonoDomain *domain, MonoImage *image_base, const char *dmeta_name, gconstpointer dmeta_bytes, uint32_t dmeta_len, const char *dil_path)
{
	const char *basename = image_base->filename;

	mono_trace (G_LOG_LEVEL_INFO, MONO_TRACE_METADATA_UPDATE, "LOADING basename=%s, dmeta=%s, dil=%s", basename, dmeta_name, dil_path);

	/* TODO: needs some kind of STW or lock */
	uint32_t generation = mono_metadata_update_prepare (domain);

	mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "base image string size: 0x%08x", image_base->heap_strings.size);


	MonoImageOpenStatus status;
	MonoImage *image_dmeta = mono_image_open_dmeta_from_data (image_base, generation, dmeta_name, dmeta_bytes, dmeta_len, &status);
	g_assert (image_dmeta);
	g_assert (status == MONO_IMAGE_OK);

	if (image_dmeta->minimal_delta) {
		guint32 idx = mono_metadata_decode_row_col (&image_dmeta->tables [MONO_TABLE_MODULE],
							    0, MONO_MODULE_NAME);

		const char *module_name = mono_metadata_string_heap (image_dmeta, idx - image_base->heap_strings.size);

		mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "dmeta name: '%s'\n", module_name);
	}

	EncRecs enc_recs;
	start_encmap (image_dmeta, &enc_recs);

	mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "base  guid: %s", image_base->guid);
	mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "dmeta guid: %s", image_dmeta->guid);

	if (mono_trace_is_traced (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE)) {
		dump_update_summary (image_base, image_dmeta, image_dmeta->minimal_delta ? image_base->heap_strings.size : 0);
	}

	MonoDilFile *dil = mono_dil_file_open (dil_path);
	image_dmeta->delta_il = dil;
	/* TODO: extend existing heaps of base image */

	MonoTableInfo *table_enclog = &image_dmeta->tables [MONO_TABLE_ENCLOG];
	int rows;
	rows = table_enclog->rows;

	if (!rows)
		
	
	for (int i = 0; i < rows ; ++i) {
		guint32 cols [MONO_ENCLOG_SIZE];
		mono_metadata_decode_row (table_enclog, i, cols, MONO_ENCLOG_SIZE);
		int log_token = cols [MONO_ENCLOG_TOKEN];
		int func_code = cols [MONO_ENCLOG_FUNC_CODE];
		g_assertf (func_code == 0, "EnC: FuncCode Default (0) is supported only, but provided: %d (token=0x%08x)", func_code, log_token);
		int table_index = mono_metadata_token_table (log_token);
		mono_trace (G_LOG_LEVEL_DEBUG, MONO_TRACE_METADATA_UPDATE, "enclog i=%d: token=0x%08x (table=%s): %d", i, log_token, mono_meta_table_name (table_index), func_code);

		if (table_index != MONO_TABLE_METHOD)
			continue;

		if (!image_base->delta_index)
			image_base->delta_index = g_hash_table_new (g_direct_hash, g_direct_equal);

		int token_idx = mono_metadata_token_index (log_token);
		int rva = mono_metadata_decode_row_col (&image_dmeta->tables [MONO_TABLE_METHOD], token_idx - 1, MONO_METHOD_RVA);

		g_hash_table_insert (image_base->delta_index, GUINT_TO_POINTER (token_idx), (gpointer) (dil->il + rva));
	}

	MonoAssemblyLoadContext *alc = mono_image_get_alc (image_base);
	mono_metadata_update_publish (domain, alc, generation);

	mono_trace (G_LOG_LEVEL_INFO, MONO_TRACE_METADATA_UPDATE, ">>> EnC delta %s (generation %d) applied", dmeta_name, generation);
}
