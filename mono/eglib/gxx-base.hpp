#ifndef _GLIB_GXX_BASE_HPP
#define _GLIB_GXX_BASE_HPP

#ifdef __cplusplus

#include <gxx-alloc.hpp>

#include <new>

namespace monoeg {
	/**
	 * Base class for Mono classes.
	 *
	 * Provides memory management operators that do not rely on global
	 * ::operator new () and ::operator delete ()
	 */
	class base {
	public:
		void*
		operator new (std::size_t sz)
		{
			return g::alloc::detail::allocate (sz);
		}

		void*
		operator new[] (std::size_t sz)
		{
			return g::alloc::detail::allocate (sz);
		}

		void*
		operator new (std::size_t sz, void* ptr)
		{
			return ::operator new (sz, ptr);
		}

		void*
		operator new[] (std::size_t sz, void* ptr)
		{
			return ::operator new[] (sz, ptr);
		}

		void
		operator delete (void* ptr)
		{
		        g::alloc::detail::deallocate (ptr);
		}

		void
		operator delete[] (void* ptr)
		{
			g::alloc::detail::deallocate (ptr);
		}


	};

	/**
	 * Base class for all polymorpic type hierarchies in the Mono runtime.
	 * This provides two things: overloaded opreator new and operator delete that use
	 * our preferred allocation functions, and a default virtual destructor.
	 *
	 * It's important to derive polymorphic base classes from polymorphic_base
	 * because GCC and Clang compile multiple symbols for a virtual destructor.
	 * One of them, the D0 "deleting object destructor" causes a dependency on
	 * the global `::operator delete(void*)` even if it is never used.  Since
	 * Mono does not link with the C++ runtime library, the mere definition of
	 * a virtual destructor will cause linking errors, unless the class has its
	 * own operator delete.
	 */
	class polymorphic_base : public base {
	public:
		virtual ~polymorphic_base () = default;
	};
}

namespace g = monoeg;

#endif /* __cplusplus */

#endif /* _GLIB_GXX_BASE_HPP */
