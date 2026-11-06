# Mobile development

The mobile UI is an early Testnet4 wallet build. It uses the upstream Wasabi logo, Wasabi's existing key manager and the repository's centrally pinned Avalonia packages. The desktop application retains its existing entry point.

The shared setup screen creates a 12-word English BIP39 wallet or recovers an English BIP39 phrase with checksum validation. The exact password is also the BIP39 passphrase, matching Wasabi's desktop behavior. The dashboard displays the first Taproot receiving address, key path and fingerprint.

The app saves one Testnet4 wallet using a local unlock password of at least 12 characters. This password is separate from the exact BIP39 recovery passphrase and does not change addresses. Creation requires confirming recovery words 1, 6 and 12. Recovery retains checksum validation. The app does not store the recovery words or either password. Android marks its activity secure to protect recovery screens from Android screenshots and recent-app previews.

The serialized KeyManager contains Wasabi password-encrypted secret material. The complete JSON is encrypted again with AES-256-GCM using PBKDF2-HMAC-SHA256 (600,000 iterations), a fresh 16-byte salt, 12-byte nonce and 16-byte authentication tag. Android adds a separate AES-GCM layer with a non-exportable Android Keystore key and writes an atomic envelope into the app no-backup directory. Hardware backing is device-dependent and is not required or claimed. iOS stores the password envelope in Keychain using WhenUnlockedThisDeviceOnly with synchronization disabled. That adapter has not been compiled or tested on a Mac.

Launching a saved wallet shows local password unlock. Lock and backgrounding remove the dashboard and stop sync before another wallet can open. A generation guard prevents a late unlock result from reopening the dashboard after backgrounding. Device storage failures and corruption fail closed; a saved wallet is never silently replaced. There is no in-app deletion, password reset or replacement flow in this preview. Reinstallation can remove Android device keys; iOS Keychain items may survive uninstall. Keep recovery words and the original recovery passphrase independently. Biometric unlock, signing, bundled Tor and CoinJoin remain pending.

## Synchronization

Start a compatible local Tor SOCKS service on 127.0.0.1 port 9050, then select Start sync on the dashboard. The app reuses the desktop peer discovery, headers, compact filters and wallet processing. Peer DNS discovery and peer connections use the configured Tor proxy. The mobile configuration rejects mainnet, disabled Tor, Bitcoin RPC and a CoinJoin coordinator.

The app displays peers and scanned/network heights. It reports a balance only after a connected wallet scan catches up with the known peer tip. Transaction IDs and block heights appear after that scan completes. A disconnected or stopped sync returns the balance display to Not synchronized.

Sync writes blockchain and transaction caches inside app storage, including public wallet transaction history. Saved keys use the protected storage described above. The saved key snapshot is written at setup; a later unlock rescans public caches. Stop cancels synchronization and closes its stores. Backgrounding also locks the wallet. Return, unlock, then select Start sync to resume.

For emulator development, a desktop Tor service can be forwarded to the emulator.

```powershell
adb reverse tcp:9050 tcp:9050
```

This forwarding is a development setup. A physical phone needs its own local Tor SOCKS service. The app currently provides no bundled Tor runtime. Desktop update checks and the JSON RPC server are disabled for mobile sync. Debug builds emit sync diagnostics through the debugger, while release builds disable logging outputs.

Android sets `UseNativeHttpHandler=false` so HTTP requests use .NET's SOCKS-capable handler. The native Java handler attempted HTTP CONNECT against the Tor SOCKS endpoint during emulator testing. The [dotnet runtime discussion](https://github.com/dotnet/runtime/issues/123050) documents this handler selection switch.

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

- Confirm the setup and dashboard show Testnet4 and device-storage status.
- Check the Wasabi logo and dashboard at narrow widths and after rotation.
- Create a wallet, acknowledge the recovery backup, then check its receiving address.
- Lock and unlock with the local password; restart the app and compare the address.
- Confirm invalid words and checksum errors leave the setup screen open.
- Tap Send and Receive and verify their status messages.
- Background and resume the app and verify the screen remains usable.
- Confirm the preview requests no camera or microphone permission.

The next integration milestones are biometric integration, native iOS verification and a locally signed testnet payment.

## Protected-storage verification

The 31 targeted MobileWallet tests passed, including reopening with a separate local password, wrong-password rejection, refusal to replace a saved wallet, malformed data, envelope tampering (salt, nonce, tag and ciphertext), fresh randomness, network validation and recovery-word checks. Android emulator checks saved the public BIP39 test vector with an empty recovery passphrase and a separate local password, restarted the process to the unlock screen, rejected an incorrect password, and reopened the original Taproot address with the correct password. Backgrounding returned to the unlock screen. The final storage APK also reopened the restored wallet, disabled keyboard suggestions in password fields, and returned to unlock after locking during active synchronization. The build completed with zero errors and five warnings (three Avalonia constructor-loader warnings and two upstream native alignment warnings). Altering a ciphertext byte in this generated test wallet caused Android Keystore AES-GCM authentication to reject the file; the preserved original was restored and its SHA-256 verified. No private user wallet was used.

The native Android file was 16,232 bytes in the no-backup directory with app-only file permissions. No recovery phrase or password is written by the storage service. Hardware key backing and biometric authentication have not been verified or implemented. The iOS Keychain adapter remains unbuilt and untested. Existing Avalonia constructor-loader and WabiSabi 16 KB native alignment warnings remain release work.

## Verification record

The synchronization integration passed 19 targeted mobile tests, including external-Tor configuration, incomplete scan balance gating and reuse of disposed worker names. A separate desktop smoke run using the same sync service completed a live Testnet4 scan at height 159472 and reported 7400 test satoshis for a public BIP39 test vector.

On 6 November 2026, the Android 11 x86_64 emulator completed a live scan through external Tor at height 159481 with six peers. The UI showed Synchronized through Tor, transaction IDs with block heights, and 0.000074 BTC (7400 test satoshis), matching the desktop smoke result. Stop closed the network services, and restarting the completed scan returned to Synchronized through Tor at height 159481 with seven peers. An earlier partial-scan stop/restart resumed successfully after fixing disposed worker registration. Peer failures and filter retries caused pauses of several minutes during this test; sync latency on unreliable peers needs further testing. That synchronization build completed with zero errors and four warnings (the two existing Avalonia loader warnings and two upstream native page-alignment warnings).

The earlier Testnet4 key-derivation integration passed ten targeted MobileWalletSessionTests, covering desktop parity for empty, ordinary and Unicode passwords, explicit BIP86 derivation, creation and recovery, exact-password handling, whitespace between recovery words and checksum rejection. All 16 existing KeyManagerTests also passed. Run the mobile checks with the existing test runner.

```powershell
dotnet run --project WalletWasabi.Tests/WalletWasabi.Tests.csproj -- --filter-class '*MobileWalletSessionTests'
```

Android emulator checks exercised recovery using a public BIP39 test vector, displayed its Taproot address, closed the session, created a new wallet, verified that backup acknowledgment gates opening, and displayed the new address. No real wallet was used.

Compatibility findings for this integration are as follows.

- KeyManager creation, recovery and address derivation work on the Android 11 x86_64 emulator.
- FilePath stays null, so the core KeyManager writes no wallet JSON file. The mobile storage service saves an authenticated encrypted envelope instead.
- Synchronization uses an external Tor SOCKS service. Bundled mobile Tor and CoinJoin remain pending.
- Android packaging warns that upstream WabiSabi 1.3.1 native libraries lack 16 KB page alignment. CoinJoin requires a compatible Android native dependency before release.
- Avalonia emits runtime-loader warnings for the two views with constructor parameters. The app constructs these views directly, and both paths were exercised on Android.
- The iOS target has not been built on a Mac. Native libraries, trimming and lifecycle behavior need separate iOS verification.
- Inspection aligned the iOS native SkiaSharp dependency with the shared managed version. NuGet's vulnerability query reported no known vulnerable packages in the Android project's resolved dependency graph at inspection time.

The earlier dashboard-only preview on 6 November 2026 built with .NET SDK 10.0.403 and Android workload 36.1.2, with zero warnings and zero errors after using SkiaSharp 3.119.4 for managed and Android native assets.

The APK was installed and launched on an Android 11 x86_64 emulator. Send and Receive changed the status message as expected, and the transaction history scrolled into view. Narrow-screen review exposed a progress-bar minimum width and button alignment issue, which were corrected. Hardware, wallet, network and authentication integration remain future work.
