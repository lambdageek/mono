# Xamarin.iOS

```
git clone --branch add-ios https://github.com/akoeplinger/runtime
cd runtime
./build.sh --restore
./build.sh --build --buildtests --os iOSSimulator
./build.sh --build --buildtests --os iOSDevice
cd ..

cd mono

echo ENABLE_IOS=1 > sdks/Make.config
echo ENABLE_NETCORE=1 >> sdks/Make.config
echo DISABLE_CLASSIC=1 >> sdks/Make.config
echo DOTNET_RUNTIME_REPO_DIR=$(cd ../runtime && pwd) >> sdks/Make.config

make -C sdks/builds build-ios -j8
make -C sdks/builds package-ios -j8

make -C sdks/ios build-ios-sim-System.Runtime.Tests
make -C sdks/ios run-ios-sim-System.Runtime.Tests

# for iOS 13 device runs we need latest ios-deploy tool from master:
brew uninstall ios-deploy
brew install --HEAD ios-deploy
```

# Xamarin.Android

(Host only for now. And probably only Darwin.)

```
cd mono

echo ENABLE_ANDROID=1 > sdks/Make.config
echo DISABLE_CLASSIC=1 >> sdks/Make.config
echo ENABLE_NETCORE=1 >> sdks/Make.config

make -C sdks/builds provision-android
make -C sdks/builds accept-android-license
make -C sdks/builds archive-android IGNORE_PROVISION_ANDROID=1 IGNORE_PROVISION_MXE=1 NINJA=
```
