#ifndef _GLIB_GXX_TRAITS_HPP
#define _GLIB_GXX_TRAITS_HPP

#ifdef __cplusplus

#include <type_traits>

namespace monoeg {
	namespace type_traits {

		/// g::type_traits::is_mempool_safe<T>::value is true iff the given type T
		/// may be allocated in a Mono mempool.
		///
		/// Because mempools do not run destructors, only trivially
		/// destructible types can be placed in mempools.
		template <typename T>
		using is_mempool_safe = std::is_trivially_destructible<T>;

	} // namespace monoeg::type_traits

} // namespace monoeg

namespace g = monoeg;

#endif /* __cplusplus */

#endif /* _GLIB_GXX_TRAITS_HPP */
