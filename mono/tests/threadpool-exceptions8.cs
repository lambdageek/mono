using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

public class Tests {

	public static int Main (string[] args) {
		return runner ();
	}

	public static int runner () {
		// need to run the test in a domain so that we can deal with unhandled exceptions
		var ad = AppDomain.CreateDomain ("Inner Domain");
		var helperType = typeof(TaskAwaiterOnCompletedHelper);
		var helper = (TaskAwaiterOnCompletedHelper)ad.CreateInstanceAndUnwrap (helperType.Assembly.ToString(), helperType.FullName);
		var result = helper.TheTest ();
		helper.AllDone();
		return result;
	}

	public class TaskAwaiterOnCompletedHelper : MarshalByRefObject {
			
		public class SpecialExn : Exception {
			public SpecialExn () : base () {}
		}
			
		public int TheTest ()
		{
			// Regression test for https://github.com/mono/mono/issues/19166
			//
			// Check that if in a call to
			// t.GetAwaiter().OnCompleted(cb) the callback cb
			// throws, that the exception's stack trace includes
			// the method that threw and not just the task
			// machinery's frames.

			// Calling "WhenCompleted" will throw "SpecialExn"
			//
			// If "OnUhandled" is installed as an unhandled exception handler, it will
			//  capture the stack trace of the SpecialExn and allow WaitForExn() to finish waiting.
			// The stack trace is expected to include ThrowerMethodInfo

			var helper = this;
			var d = new UnhandledExceptionEventHandler (helper.OnUnhandled);
			AppDomain.CurrentDomain.UnhandledException += d;

			// this is TaskToApm.Begin (..., callback) where the callback is helper.WhenCompleted
			Task.Delay (100).GetAwaiter().OnCompleted (helper.WhenCompleted);

			var wasSet = helper.WaitForExn (10000); // wait upto 10 seconds for the task to throw

			AppDomain.CurrentDomain.UnhandledException -= d;

			if (!wasSet) {
				Console.WriteLine ("event not set, no exception thrown?");
				return 1;
			}

			var frames = helper.CapturedStackTraceFrames;

			if (frames == null)
				return 2;

			bool found = false;
			foreach (var frame in frames) {
				if (frame.GetMethod ().Equals (helper.ThrowerMethodInfo)) {
					found = true;
					break;
				}
			}
			if (!found) {
				Console.WriteLine ("expected to see {0} in stack trace, but it wasn't there", helper.ThrowerMethodInfo.ToString());
				return 3;
			}

			return 0;

		}

		private ManualResetEventSlim coord;
		private ManualResetEventSlim coord2;

		private StackFrame[] frames;
			
		public TaskAwaiterOnCompletedHelper ()
		{
			coord = new ManualResetEventSlim ();
			coord2 = new ManualResetEventSlim ();
		}

		public MethodBase ThrowerMethodInfo => typeof(TaskAwaiterOnCompletedHelper).GetMethod (nameof (WhenCompletedThrower));

		[MethodImpl (MethodImplOptions.NoInlining)]
		public void WhenCompleted ()
		{
			WhenCompletedThrower ();
		}

		[MethodImpl (MethodImplOptions.NoInlining)]
		public void WhenCompletedThrower ()
		{
			throw new SpecialExn ();
		}

		public void OnUnhandled (object sender, UnhandledExceptionEventArgs args)
		{
			if (args.ExceptionObject is SpecialExn exn) {
				var trace = new StackTrace (exn);
				frames = trace.GetFrames ();
				coord.Set ();
				coord2.Wait ();
			}
		}

		public StackFrame[] CapturedStackTraceFrames => frames;


		public bool WaitForExn (int timeoutMilliseconds)
		{
			return coord.Wait (timeoutMilliseconds);
		}

		public void AllDone ()
		{
			coord2.Set ();
		}
	}
}
