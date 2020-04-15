#!/bin/bash

echo "warning for dotnet/runtime noobs. to rebuild, do"
echo '   > $ ./mono.sh'
echo '   > $ make -C src/mono/netcore patch-mono-dotnet'
echo 'in the repository root'

set -e
set -x

if [ x`uname -s` = xDarwin ]; then
    PLAT=OSX
else
    PLAT=Linux
fi
DNILASM=artifacts/bin/coreclr/${PLAT}.x64.Debug/ilasm
DNILDASM=artifacts/bin/coreclr/${PLAT}.x64.Debug/ildasm

d=`pwd`
m=${d}/../..
ILASM=${d}/ilasm
ILDASM=${d}/ildasm
CSC=csc
MONODOTNET=../../../.dotnet-mono/dotnet
MONOMONO=${m}/mono/mini/mono

: "${NAME:=Sample1}"
OUT=here

if [[ ! -f $ILASM ]]; then
    echo "please build everything in dotnet/runtime by calling ./build.sh. we need ilasm which is part of CoreCLR";
    echo "and copy ${DNILASM} and ${DNILDASM} to the current directory"
    exit 1;
fi


$CSC $NAME.cs

$ILDASM $NAME.exe > $NAME.il
rm $NAME.exe

ILASM_ARGS=""
for arg in ${NAME}_v?.il; do
	ILASM_ARGS="$ILASM_ARGS -ENC=$arg"
done

rm $OUT.*
$ILASM -DEBUG -OUT=$OUT.exe $NAME.il $ILASM_ARGS

cp template.runtimeconfig.json $OUT.runtimeconfig.json

#MONO_VERBOSE_METHOD=Do MONO_ENV_OPTIONS='--debug --interpreter' lldb -- $MONODOTNET $OUT.exe
MONO_LOG_MASK=metadata-update MONO_LOG_LEVEL=debug MONO_VERBOSE_METHOD=Do MONO_ENV_OPTIONS='--debug --interp=-inline' MONO_PATH=${m}/mcs/class/lib/net_4_x MONO_CONFIG=${m}/runtime/etc/mono/config lldb -- $MONOMONO $OUT.exe
