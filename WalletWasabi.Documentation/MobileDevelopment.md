# Mobile development

The mobile UI is an early Testnet4 wallet build. It uses the upstream Wasabi logo, Wasabi's existing key manager and the repository's centrally pinned Avalonia packages. The desktop application retains its existing entry point.

The shared setup screen creates a 12-word English BIP39 wallet or recovers an English BIP39 phrase with checksum validation. The exact password is also the BIP39 passphrase, matching Wasabi's desktop behavior. The dashboard displays the first Taproot receiving address, key path and fingerprint.

This build keeps the key manager in memory for the session. It writes no wallet file or recovery phrase. Save the words and exact password before closing. The backup acknowledgment is an initial step, and an interactive word verification exercise remains planned. Persistent protected storage, balance synchronization, signing, Tor, CoinJoin and authentication are future work. Android marks its activity secure to protect recovery screens from Android screenshots and recent-app previews.

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

- Confirm the setup and dashboard show Testnet4 and session-only status.
- Check the Wasabi logo and dashboard at narrow widths and after rotation.
- Create a wallet, acknowledge the recovery backup, then check its receiving address.
- Close and recover using the same words and exact password and compare the address.
- Confirm invalid words and checksum errors leave the setup screen open.
- Tap Send and Receive and verify their status messages.
- Background and resume the app and verify the screen remains usable.
- Confirm the preview requests no camera or microphone permission.

The dashboard reports the balance as unsynchronized. The next integration milestone is protected persistence followed by synchronization and a locally signed testnet payment on each mobile platform.

## Verification record

The next local build integrates Testnet4 key derivation. Ten targeted MobileWalletSessionTests passed, covering desktop parity for empty, ordinary and Unicode passwords, explicit BIP86 derivation, creation and recovery, exact-password handling, whitespace between recovery words and checksum rejection. All 16 existing KeyManagerTests also passed. Run the mobile checks with the existing test runner.

```powershell
dotnet run --project WalletWasabi.Tests/WalletWasabi.Tests.csproj -- --filter-class '*MobileWalletSessionTests'
```

Android emulator checks exercised recovery using a public BIP39 test vector, displayed its Taproot address, closed the session, created a new wallet, verified that backup acknowledgment gates opening, and displayed the new address. No real wallet was used.

Compatibility findings for this integration are as follows.

- KeyManager creation, recovery and address derivation work on the Android 11 x86_64 emulator.
- FilePath is left null, so KeyManager writes no wallet file. Persistent protected storage remains pending.
- Tor, synchronization and CoinJoin have no mobile runtime integration.
- Android packaging warns that upstream WabiSabi 1.3.1 native libraries lack 16 KB page alignment. CoinJoin requires a compatible Android native dependency before release.
- Avalonia emits runtime-loader warnings for the two views with constructor parameters. The app constructs these views directly, and both paths were exercised on Android.
- The iOS target has not been built on a Mac. Native libraries, trimming and lifecycle behavior need separate iOS verification.
- Inspection aligned the iOS native SkiaSharp dependency with the shared managed version. NuGet's vulnerability query reported no known vulnerable packages in the Android project's resolved dependency graph at inspection time.

The earlier dashboard-only preview on 6 November 2026 built with .NET SDK 10.0.403 and Android workload 36.1.2, with zero warnings and zero errors after using SkiaSharp 3.119.4 for managed and Android native assets.

The APK was installed and launched on an Android 11 x86_64 emulator. Send and Receive changed the status message as expected, and the transaction history scrolled into view. Narrow-screen review exposed a progress-bar minimum width and button alignment issue, which were corrected. Hardware, wallet, network and authentication integration remain future work.
