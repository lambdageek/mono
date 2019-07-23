#ifndef _GLIB_GXX_ALLOC_HPP
#define _GLIB_GXX_ALLOC_HPP

#ifdef __cplusplus

#include <cstddef>
#include <utility>

namespace monoeg {
	namespace alloc {
		/// Provides allocation primitives for Mono's base library
		namespace detail {

			inline void*
			allocate (std::size_t sz)
			{
				return g_malloc (sz);
			}

			inline void
			deallocate (void* ptr)
			{
				g_free (ptr);
			}
		} // namespace monoeg::alloc::detail
	} // namespace monoeg::alloc

	/**
	 * Use the new_ function to allocate:
	 *    class Foo : public g::polymorphic_base {}
	 *    class Bar : public Foo { public: Bar(int x, double y); }
	 *    Foo *foo = g::new_<Bar> (1, 2.0);
	 *    g::delete_ (foo);
	 *
	 * Use g::delete_ to delete objects allocated with new_.
	 *
	 * This template can be used with user-defined classes, as well as with the
	 * builtin types such as 'bool', 'char', 'int', 'Foo*' etc.
	 */
	template <typename T, typename... Args>
	inline
	T*
	new_ (Args&&... args) {
		void *ptr = monoeg::alloc::detail::allocate (sizeof (T));
		return !ptr ? nullptr : new (ptr) T (std::forward<Args> (args)...);
	}

	/**
	 * Use the delete_ function to deallocate objects allocated with mono::new_
	 *
	 *    class Foo : public g::polymorphic_base {}
	 *    class Bar : public Foo { public: Bar (int x, double y); }
	 *    Foo *foo = g::new_<Bar> (1, 2.0);
	 *    mono::delete_ (foo);
	 *
	 * This template can be used with user-defined clases, as well as with
	 * the builtin types such as 'bool', 'char', 'int', 'Foo*' etc.
	 *
	 */
	template <typename T>
	inline
	void
	delete_ (T* t) {
		if (t == nullptr)
			return;
		void *ptr = static_cast<void*>(t);
		t->~T ();
		monoeg::alloc::detail::deallocate (ptr);
	}

} // namespace monoeg

namespace g = monoeg;

#endif /* __cplusplus */

#endif /* _GLIB_GXX_ALLOC_HPP */
