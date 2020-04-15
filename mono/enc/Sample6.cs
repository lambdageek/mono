using System;
using System.Reflection;
using System.Runtime.CompilerServices;

public class Sample {
	public static void Main (string []args) {
		Do ();
		Do ();
	}

	private static int counter = 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void Do () {
		if (counter == 0) {
			counter++;
			Console.WriteLine ("Hello 1");
			ApplyMagicMethodBodyReplacement ();
			Console.WriteLine ("Hello 2");
			Console.WriteLine ("Hello 3");
		}
		Console.WriteLine ("Hello 4");
	}

	private static void ApplyMagicMethodBodyReplacement () {
#if false
		var name = "System.Runtime.CompilerServices.RuntimeFeature";
#else
		var name = "Mono.Runtime";
#endif
		var monoType = Type.GetType (name, false);
		try {
			var update = monoType.GetMethod("LoadMetadataUpdate");
			update.Invoke (null, new object[] { typeof(Sample).Assembly });
		} catch (Exception e) {
			Console.WriteLine ("the impossible happen: " + e);
		}
	}
}
