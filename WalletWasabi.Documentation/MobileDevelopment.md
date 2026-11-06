# Mobile development

The mobile UI is an early preview with labeled sample data. It uses the upstream Wasabi logo and the repository's centrally pinned Avalonia packages. The desktop application retains its existing entry point.

The shared dashboard supports Android and iOS single-view lifetimes. Send and Receive display explanatory status text. Wallet creation, keys, signing, storage, Tor, CoinJoin and authentication are future integration work.

## Android

Use the .NET SDK selected by the root global.json and install the Android workload.

```powershell
dotnet workload install android
dotnet build WalletWasabi.Mobile.Android/WalletWasabi.Mobile.Android.csproj -t:InstallAndroidDependencies -p:AndroidSdkDirectory=C:/dev/android-sdk -p:JavaSdkDirectory=C:/dev/jdk
dotnet build WalletWasabi.Mobile.Android/WalletWasabi.Mobile.Android.csproj -p:AndroidSdkDirectory=C:/dev/android-sdk -p:JavaSdkDirectory=C:/dev/jdk
```

Review and accept the required SDK licenses when prompted. Adjust the absolute SDK paths to match your machine. InstallAndroidDependencies can also accept licenses using AcceptAndroidSdkLicenses=True.

To install a Debug APK on a connected Android device or a running emulator, use the same SDK arguments with the Install target.

```powershell
dotnet build WalletWasabi.Mobile.Android/WalletWasabi.Mobile.Android.csproj -t:Install -p:AndroidSdkDirectory=C:/dev/android-sdk -p:JavaSdkDirectory=C:/dev/jdk
```

Launch Wasabi Mobile Preview from the device launcher. APKs are under the Android project's bin directory.

## iOS

The iOS target requires a Mac, a compatible Xcode installation and the .NET iOS workload. Build the simulator target from that Mac.

```sh
dotnet workload install ios
dotnet build WalletWasabi.Mobile.iOS/WalletWasabi.Mobile.iOS.csproj -p:RuntimeIdentifier=iossimulator-arm64
```

Physical iPhone deployment additionally requires provisioning and signing. iOS builds have not yet been verified on a Mac.

## First launch checks

- Confirm the screen labels all balances and rates as sample data.
- Check the Wasabi logo and dashboard at narrow widths and after rotation.
- Tap Send and Receive and verify the explanatory text updates.
- Background and resume the app and verify the screen remains usable.
- Confirm the preview requests no camera or microphone permission.

The preview has no wallet secrets or backend integration. The self-custody milestone will require recovery compatibility and a locally signed testnet payment on each mobile platform.

## Verification record

On 6 November 2026, the shared UI and Android Debug APK built with .NET SDK 10.0.403 and Android workload 36.1.2. The Android build completed with zero warnings and zero errors after using the repository's SkiaSharp 3.119.4 version for managed and Android native assets.

The APK was installed and launched on an Android 11 x86_64 emulator. Send and Receive changed the status message as expected, and the transaction history scrolled into view. Narrow-screen review exposed a progress-bar minimum width and button alignment issue, which were corrected. Hardware, wallet, network and authentication integration remain future work.
