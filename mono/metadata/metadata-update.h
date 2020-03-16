/**
 * \file
 */

#ifndef __MONO_METADATA_UPDATE_H__
#define __MONO_METADATA_UPDATE_H__

#include "mono/utils/mono-forward.h"
#include "mono/metadata/loader-internals.h"

gboolean
mono_metadata_update_available (void);

gboolean
mono_metadata_wait_for_update (uint32_t timeout_ms);

uint32_t
mono_metadata_update_prepare (MonoDomain *domain);

void
mono_metadata_update_publish (MonoDomain *domain, MonoAssemblyLoadContext *alc, uint32_t generation);


#endif /*__MONO_METADATA_UPDATE_H__*/
