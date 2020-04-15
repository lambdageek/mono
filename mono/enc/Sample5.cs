using System;
using System.Reflection;
using System.Runtime.CompilerServices;

public class Sample {
	public static void Main (string []args) {
		Calculate ();

		ApplyMagicMethodBodyReplacement ();

		Calculate ();
	}

	private static void Calculate () {
		for (int i = 1; i < 5; i++)
			for (int j = 1; j < 4; j++)
				Console.WriteLine ("Do (" + i + ", " + j + "): " + Do (i, j));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Do (int x, int y) {
		return x + y;
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
