using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

public class Sample {
	public static int Main (string[] args) {
		if (args.Length != 1) {
			Console.Error.WriteLine ("Usage: SampleRunner ASSEMBLY_PATH");
			return 1;
		}

		Assembly assm = Assembly.LoadFrom (Path.GetFullPath (args[0]));

		var calc = Calculator.Make (assm, "RoslynILDiff", "DiffTestMethod1");

		var replacer = Replacer.Make ();
		Calculate (calc);

		replacer.Update (assm);

		Calculate (calc);

#if true
		replacer.Update (assm);

		Calculate (calc);
#endif

		return 0;
	}

	private static void Calculate (Calculator calc) {
#if true 
		for (int i = 1; i < 5; i++)
			for (int j = 1; j < 4; j++)
				Console.WriteLine ("Do (" + i + ", " + j + "): " + calc.Do (i, j));
#else
		Console.WriteLine ($"D() = {calc.Do ()}");
#endif
	}
}

public class Calculator  {
	MethodBase m;

	Calculator (MethodBase m) {
		this.m = m;
	}

	public static Calculator Make (Assembly assm, string typeName, string methName) {
		var ty = assm.GetType (typeName, true);
		var mi = ty.GetMethod (methName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static );
		return new Calculator (mi);
	}

	public object Do (params object[] args) {
		return m.Invoke (null, args);
	}
}

public class Replacer {
#if false
	const string name = "System.Runtime.CompilerServices.RuntimeFeature";
#else
	const string name = "Mono.Runtime";
#endif
	private static MethodBase _updateMethod;

	private static MethodBase UpdateMethod => _updateMethod ?? InitUpdateMethod();

	private static MethodBase InitUpdateMethod ()
	{
		var monoType = Type.GetType (name, false);
		_updateMethod = monoType.GetMethod ("LoadMetadataUpdate");
		if (_updateMethod == null)
			throw new Exception ($"Couldn't get LoadMetadataUpdate from {name}");
		return _updateMethod;
	}

	Replacer () { }

	public static Replacer Make ()
	{
		return new Replacer ();
	}

	private Dictionary<Assembly, int> assembly_count = new Dictionary<Assembly, int> ();

	public void Update (Assembly assm) {
		Console.WriteLine ("Apply Delta Update");

		int count;
		if (!assembly_count.TryGetValue (assm, out count))
			count = 1;
		else
			count++;
		assembly_count [assm] = count;

		string basename = assm.Location;
		string dmeta_name = $"{basename}.{count}.dmeta";
		string dil_name = $"{basename}.{count}.dil";
		byte[] dmeta_data = System.IO.File.ReadAllBytes (dmeta_name);
		byte[] dil_data = System.IO.File.ReadAllBytes (dil_name);

		UpdateMethod.Invoke (null, new object [] { assm, dmeta_data, dil_data});
	}
}
