
// Metadata version: v4.0.30319


// =============== CLASS MEMBERS DECLARATION ===================

.class public auto ansi beforefieldinit Sample
       extends [mscorlib]System.Object
{

    .method private static hidebysig 
           default class [mscorlib]System.Type Do ()  cil managed noinlining 
    {
	// Code size 16 (0x10)
	.maxstack 1
	.locals init (
		class [mscorlib]System.Type	V_0)
	IL_0000:  nop 
	IL_0001:  ldtoken [mscorlib]System.Reflection.Assembly
	// was this meant? doesn't matter really
	// IL_0001:  ldtoken Class2
	IL_0006:  call class [mscorlib]System.Type class [mscorlib]System.Type::GetTypeFromHandle(valuetype [mscorlib]System.RuntimeTypeHandle)
	IL_000b:  stloc.0 
	IL_000c:  br.s IL_000e

	IL_000e:  ldloc.0 
	IL_000f:  ret 
    } // end of method Sample::Do

} // end of class Sample


