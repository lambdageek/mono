
// Metadata version: v4.0.30319


// =============== CLASS MEMBERS DECLARATION ===================

.class public auto ansi beforefieldinit Sample
       extends [System.Runtime]System.Object
{

	.method private hidebysig static int32
		NotHereYet(int32 x) cil managed noinlining
		{
			.maxstack 2
			ldarg.0
			ldarg.0
			mul
			ret
		}

  .method private hidebysig static int32 
          Do(int32 x,
             int32 y) cil managed noinlining
  {
    // Code size       9 (0x9)
    .maxstack  2
    .locals init (int32 V_0)
    IL_0000:  nop
    IL_0001:  ldarg.0
              call int32 Sample::NotHereYet(int32)
    IL_0002:  ldarg.1
              call int32 Sample::NotHereYet(int32)
    IL_0003:  add
    IL_0004:  stloc.0
    IL_0005:  br.s       IL_0007

    IL_0007:  ldloc.0
    IL_0008:  ret
  } // end of method Sample::Do

} // end of class Sample


