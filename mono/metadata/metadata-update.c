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
#include "mono/utils/mono-path.h"

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


void
mono_image_load_enc_delta (MonoDomain *domain, MonoImage *image_base, char *basename, char *dmeta, char *dil)
{
	int rows;

	g_print ("LOADING basename=%s, dmeta=%s, dil=%s\n", basename, dmeta, dil);

	/* TODO: needs some kind of STW or lock */
	uint32_t generation = mono_metadata_update_prepare (domain);

	/* TODO: bad assumption, can be a different assembly than the main one */
	/*   is it possible to find base image by GUID?
	 *   yeah, but doesn't exist in netcore:
	 *   > mono_image_loaded_by_guid (const char *guid)
	 */
	MonoAssemblyLoadContext *alc = mono_image_get_alc (image_base);
	g_assert (!strcmp (mono_path_resolve_symlinks (basename), image_base->filename));


	MonoImageOpenStatus status;
	/* TODO: helper? */
	MonoImage *image_dmeta = mono_image_open_metadata_only (alc, dmeta, &status);

	g_print ("base  guid: %s\n", image_base->guid);

	guint32 module_cols [MONO_MODULE_SIZE];
	/* TODO: why is image_dmeta->guid not initialized? */
	mono_metadata_decode_row (&image_dmeta->tables [MONO_TABLE_MODULE], 0, module_cols, MONO_MODULE_SIZE);
	g_print ("module_cols [MONO_MODULE_MVID]: %d\n", module_cols [MONO_MODULE_MVID]);
	const char *dmeta_mvid = mono_guid_to_string ((guint8 *) mono_metadata_guid_heap (image_dmeta, module_cols [MONO_MODULE_MVID]));
	g_print ("dmeta guid: %s\n", dmeta_mvid);

	rows = mono_image_get_table_rows (image_base, MONO_TABLE_METHOD);
	for (int i = 1; i <= rows ; ++i) {
		ERROR_DECL (error);
		int token = MONO_TOKEN_METHOD_DEF | i;
		MonoMethod *method = mono_get_method_checked (image_base, token, NULL, NULL, error);
		g_print ("base  method %d (token=0x%08x): %s\n", i, token, mono_method_get_name_full (method, TRUE, TRUE, MONO_TYPE_NAME_FORMAT_IL));
	}

	for (int i = 0; i < 0x10; i++) g_print ("==");
	g_print ("\n");

	rows = mono_image_get_table_rows (image_base, MONO_TABLE_METHOD);
	for (int i = 1; i <= rows ; ++i) {
		guint32 cols [MONO_METHOD_SIZE];
		mono_metadata_decode_row (&image_base->tables [MONO_TABLE_METHOD], i - 1, cols, MONO_METHOD_SIZE);
		const char *name = mono_metadata_string_heap (image_base, cols [MONO_METHOD_NAME]);
		guint32 rva = cols [MONO_METHOD_RVA];
		g_print ("base  method i=%d, rva=%d/0x%04x, name=%s\n", i, rva, rva, name);
	}

	for (int i = 0; i < 0x10; i++) g_print ("==");
	g_print ("\n");

	rows = mono_image_get_table_rows (image_dmeta, MONO_TABLE_METHOD);
	for (int i = 1; i <= rows ; ++i) {
		guint32 cols [MONO_METHOD_SIZE];
		mono_metadata_decode_row (&image_dmeta->tables [MONO_TABLE_METHOD], i - 1, cols, MONO_METHOD_SIZE);
		const char *name = mono_metadata_string_heap (image_dmeta, cols [MONO_METHOD_NAME]);
		guint32 rva = cols [MONO_METHOD_RVA];
		g_print ("dmeta method i=%d, rva=%d/0x%04x, name=%s\n", i, rva, rva, name);
	}

	for (int i = 0; i < 0x10; i++) g_print ("==");
	g_print ("\n");


	MonoFileMap *il_delta_filed = mono_file_map_open (dil);
	g_assert (il_delta_filed);
	gpointer handle;
	char *il_delta = (char *) mono_file_map_fileio (
			mono_file_map_size (il_delta_filed),
			MONO_MMAP_READ | MONO_MMAP_PRIVATE,
			mono_file_map_fd (il_delta_filed),
			0,
			&handle);
	/* TODO: extend existing heaps of base image */

	MonoTableInfo *table_enclog = &image_dmeta->tables [MONO_TABLE_ENCLOG];
	rows = table_enclog->rows;
	for (int i = 0; i < rows ; ++i) {
		guint32 cols [MONO_ENCLOG_SIZE];
		mono_metadata_decode_row (table_enclog, i, cols, MONO_ENCLOG_SIZE);
		int log_token = cols [MONO_ENCLOG_TOKEN];
		int func_code = cols [MONO_ENCLOG_FUNC_CODE];
		g_assertf (func_code == 0, "EnC: FuncCode Default (0) is supported only, but provided: %d (token=0x%08x)", func_code, log_token);
		int table_index = mono_metadata_token_table (log_token);
		g_print ("enclog i=%d: token=0x%08x (table=%s): %d\n", i, log_token, mono_meta_table_name (table_index), func_code);

		if (table_index != MONO_TABLE_METHOD)
			continue;

		if (!image_base->delta_index)
			image_base->delta_index = g_hash_table_new (g_direct_hash, g_direct_equal);

		int token_idx = mono_metadata_token_index (log_token);
		int rva = mono_metadata_decode_row_col (&image_dmeta->tables [MONO_TABLE_METHOD], token_idx - 1, MONO_METHOD_RVA);

		g_hash_table_insert (image_base->delta_index, GUINT_TO_POINTER (token_idx), (gpointer) (il_delta + rva));
	}

	mono_metadata_update_publish (domain, alc, generation);

	g_print (">>> EnC delta applied\n");
	fflush (stdout);
}
