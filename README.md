# Wasabi Wallet Mobile

An independent iOS and Android port of Wasabi Wallet v2.8.5. This repository starts with the upstream desktop source at commit `86ea7242ca218c1d576db2756bb22dc165cc7325` and the mobile project scope below. A shared sample dashboard and Android and iOS app targets are now available. Wallet integration is planned. See the [mobile development guide](WalletWasabi.Documentation/MobileDevelopment.md) for build commands and current limitations.

- Reuse the C# wallet engine and Avalonia interface, including the Wasabi logo, styles, themes, and desktop workflows.
- Target wallet creation and recovery, send and receive, QR scanning, transaction history, coin control, fees, Tor, and CoinJoin.
- Connect compatible hardware wallets and verify receiving addresses and payment details on the signing device. Offer watch-only wallets.
- Add encrypted storage backed by Keychain or Keystore, app lock, and separate system authentication for payments and sensitive actions. Use supported face, fingerprint, or iris verification with a passcode fallback.
- Verify backups with a practice restore and show an on-device password strength indicator with Weak, Strong, and Very strong ratings.
- Add Tor Required mode, Discreet Mode, payment warnings, and an optional local security activity history.
- Add opt-in push-to-talk navigation and permitted readouts. Payment signing requires separate authentication or hardware approval.

The first mobile milestone is recovering the same testnet wallet on desktop and both phones, checking matching addresses and balance, then reviewing and sending a testnet payment through Tor on each phone.

[Upstream source](https://github.com/WalletWasabi/WalletWasabi/tree/v2.8.5) · [License](LICENSE.md)

## Upstream README
<p align="center">
  <a href="https://wasabiwallet.io">
    <img src="https://github.com/WalletWasabi/WalletWasabi/blob/master/ui-ww.png">
  </a>
</p>

<h3 align="center">
    An open-source, non-custodial, privacy-focused Bitcoin wallet for desktop.
</h3>

<h3 align="center">
  <a href="https://wasabiwallet.io">
    Website
  </a>
  <span> | </span>
  <a href="https://docs.wasabiwallet.io/">
    Documentation
  </a>
  <span> | </span>
  <a href="https://github.com/WalletWasabi/WalletWasabi/discussions/5185">
    Support
  </a>
  <span> | </span>
  <a href="https://www.youtube.com/c/WasabiWallet">
    YouTube
  </a>
  <span> | </span>
  <a href="https://github.com/WalletWasabi/WalletWasabi/blob/master/PGP.txt">
    PGP
  </a>
</h3>
<br>

# [Download Wasabi](https://github.com/WalletWasabi/WalletWasabi/releases)

<br>

# Build From Source Code

### Get The Requirements

1. Get Git: https://git-scm.com/downloads
2. Get .NET 10.0 SDK: https://dotnet.microsoft.com/download
3. Optionally disable .NET's telemetry by executing in the terminal `export DOTNET_CLI_TELEMETRY_OPTOUT=1` on Linux and macOS or `setx DOTNET_CLI_TELEMETRY_OPTOUT 1` on Windows.

### Get Wasabi

Clone & Restore & Build

```sh
git clone --depth=1 --single-branch --branch=master https://github.com/WalletWasabi/WalletWasabi.git
cd WalletWasabi/WalletWasabi.Fluent.Desktop
dotnet build
```

### Run Wasabi

Run Wasabi with `dotnet run` from the `WalletWasabi.Fluent.Desktop` folder.

### Update Wasabi

```sh
git pull
```

