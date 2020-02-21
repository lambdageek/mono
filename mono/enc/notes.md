```console
$ cat Sample.il
[..]
    IL_0000:  nop
    IL_0001:  ldarg.0
    IL_0002:  ldarg.1
    IL_0003:  add
    IL_0004:  stloc.0
    IL_0005:  br.s       IL_0007

    IL_0007:  ldloc.0
    IL_0008:  ret
[...]

$ cat Sample_v1.il   # The IL body that should be used after the IL update is applied.
[...]
    IL_0000:  nop                  // opcode = 0x00
    IL_0001:  ldarg.0              // opcode = 0x02
    IL_0002:  ldarg.1              // opcode = 0x03
    IL_0003:  mul                  // opcode = 0x5a
    IL_0004:  stloc.0              // opcode = 0x0a
    IL_0005:  br.s       IL_0007   // opcode = 0x2b <int8>

    IL_0007:  ldloc.0              // opcode = 0x06
    IL_0008:  ret                  // opcode = 0x2a
[...]
} // end of class Sample


$ xxd here.exe.1.dil    # dil = "Delta IL"
00000000: 1500 0000 1330 0200 0900 0000 0200 0011  .....0..........
00000010: 0002 035a 0a2b 0006 2a                   ...Z.+..*
```


# Notes on file datastructures

`.dmeta` contains metadata deltas that should be appended to the existing
heaps, how exactly is described by ENCLog.

`.dil` contains new IL method bodies, where `.dmeta` encodes the proper offsets
(RVAs) into that file for each method that needs to be updated.

Consequently, the RVA for method in the example above is 0x4, where the fat
header starts.

# ENCLog

This is a new table (not in the ECMA spec). Each row is defined like this:

```c
  struct enclog_row {
  	uint32 token; // full token
  	int funccode; // valid values are 0 to 5
  };
```

`token` is a reference to the token in the original image. For what we care
about for now, `funccode` can only be `0` and means we need to replace the
existing entry with the entry from the `.dmeta` table (same token).

# ENCMap

Not sure why this is needed.
```
  struct encmap_row {
  	uint32 token; // full token
  };
```

The minimal example doesn't contain a ENCMap table.  Why?

# References

check out CoreCLR code:
	> src/coreclr/src/md/enc/metamodelenc.cpp:ApplyDelta()  <- metadata fun
	> src/coreclr/src/vm/encee.cpp  <- entry point called by debugger interface
	> src/coreclr/src/vm/encee.cpp#L221-L234  <- does the updating of IL
