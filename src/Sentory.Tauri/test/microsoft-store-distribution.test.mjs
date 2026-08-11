import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import test from "node:test";

const read = relativePath => readFileSync(
  new URL(relativePath, import.meta.url),
  "utf8",
);

const html = read("../web/index.html");
const script = read("../web/app.js");
const styles = read("../web/styles.css");
const rust = read("../src-tauri/src/main.rs");
const cargo = read("../src-tauri/Cargo.toml");
const manifest = read("../../../installer/msix/AppxManifest.xml.template");
const buildScript = read("../../../scripts/Build-Tauri.ps1");
const bundleScript = read("../../../scripts/Publish-MsixStoreBundle.ps1");
const packageScript = read("../../../scripts/Publish-MsixPackage.ps1");
const certificateScript = read("../../../scripts/New-MsixTestCertificate.ps1");
const installTestPackageScript = read("../../../scripts/Install-MsixTestPackage.ps1");
const workflow = read("../../../.github/workflows/tauri-msix-store.yml");
const msixAssetsUrl = new URL("../../../installer/msix/Assets/", import.meta.url);

test("the Store channel has no in-app update path", () => {
  assert.match(html, /id="update-setting-row"[^>]*hidden/);
  assert.match(script, /distribution_channel/);
  assert.match(script, /updateSettingRow\.hidden = channel !== "github"/);
  assert.match(styles, /\.setting-row\[hidden\]\s*\{\s*display:\s*none/);
  assert.match(rust, /if is_microsoft_store\(\)[\s\S]{0,180}앱 내 업데이트 확인을 제공하지 않습니다/);
});

test("Store packages are built from Store-channel source for x64 and ARM64", () => {
  assert.match(cargo, /microsoft-store\s*=\s*\[\]/);
  assert.match(buildScript, /\$features \+= "microsoft-store"/);
  assert.match(buildScript, /\$tauriArguments \+= @\("--features"/);
  assert.match(bundleScript, /\[string\[\]\]\$Architectures = @\("x64", "arm64"\)/);
  assert.match(bundleScript, /foreach \(\$architecture in \$Architectures\)/);
  assert.match(bundleScript, /-DistributionChannel MicrosoftStore/);
  assert.doesNotMatch(bundleScript, /PayloadArchive|portable\.zip/i);
  assert.match(buildScript, /\$DistributionChannel -eq "GitHub" -and/);
  assert.match(workflow, /runs-on: windows-11-vs2026-arm/);
  assert.match(workflow, /Publish-MsixStoreBundle\.ps1/);
  assert.doesNotMatch(workflow, /gh release|release upload/i);
});

test("Store bundle keeps the requested four-part package version", () => {
  assert.match(
    bundleScript,
    /bundle \/d \$bundleInput \/p \$bundlePath \/bv \$PackageVersion \/o \/v/,
  );
});

test("local executable MSIX review uses a signed test channel", () => {
  assert.match(bundleScript, /\[switch\]\$SignedTest/);
  assert.match(bundleScript, /if \(\$SignedTest -and \[string\]::IsNullOrWhiteSpace\(\$CertificateThumbprint\)\)/);
  assert.match(bundleScript, /channel = if \(\$UnsignedTest -or \$SignedTest\)/);
  assert.match(certificateScript, /1\.3\.6\.1\.5\.5\.7\.3\.3/);
  assert.match(certificateScript, /2\.5\.29\.19=\{text\}/);
  assert.match(certificateScript, /KeyExportPolicy NonExportable/);
  assert.match(installTestPackageScript, /Cert:\\LocalMachine\\TrustedPeople/);
  assert.match(installTestPackageScript, /Get-AuthenticodeSignature/);
  assert.match(installTestPackageScript, /Add-AppxPackage -Path \$bundle/);
  assert.doesNotMatch(installTestPackageScript, /AllowUnsigned/);
});

test("MSIX packaging rejects a stale GitHub-channel executable", () => {
  assert.match(rust, /VERIFY_MICROSOFT_STORE_CHANNEL_ARGUMENT/);
  assert.match(rust, /--verify-microsoft-store-channel/);
  assert.match(bundleScript, /--verify-microsoft-store-channel/);
  assert.match(bundleScript, /ExitCode -ne 73/);
  assert.match(bundleScript, /Microsoft Store 채널 실행 파일이 아닙니다/);
});

test("Store startup uses the declared StartupTask instead of the Run key", () => {
  assert.match(manifest, /Category="windows\.startupTask"/);
  assert.match(manifest, /TaskId="SentoryStartupTask"/);
  assert.match(manifest, /Enabled="false"/);
  assert.match(rust, /if is_microsoft_store\(\) \{[\s\S]*?read_store_startup_enabled\(\)\.await/);
  assert.match(rust, /RequestEnableAsync/);
});

test("Store Discord startup coordination uses explicit registry exceptions", () => {
  assert.match(manifest, /desktop6:RegistryWriteVirtualization>disabled/);
  assert.match(manifest, /HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Run/);
  assert.match(manifest, /HKEY_CURRENT_USER\\Software\\Sentory\\DiscordStartupBackup/);
  assert.match(manifest, /rescap:Capability Name="unvirtualizedResources"/);
});

test("Store keeps durable user data outside disposable local package data", () => {
  assert.match(rust, /"SENTORY_DATA_ROOT"[\s\S]{0,100}store_durable_data_root/);
  assert.match(rust, /"SENTORY_LOCAL_DATA_ROOT"[\s\S]{0,100}store_local_data_root/);
  assert.match(rust, /local_app_data\.join\("Sentory"\)/);
  assert.match(rust, /store_application_data_root\(\)\?\.join\("Sentory"\)/);
});

test("Store runtime does not expose the legacy Discord startup restore utility", () => {
  assert.match(
    rust,
    /RESTORE_DISCORD_STARTUP_ARGUMENT[\s\S]*?if is_microsoft_store\(\) \{[\s\S]*?exit\(2\)/,
  );
});

test("MSIX provides exact unplated icons for Windows shell surfaces", () => {
  const assetNames = new Set(readdirSync(msixAssetsUrl));
  const targetSizes = [16, 20, 24, 30, 32, 36, 40, 44, 48, 60, 64, 72, 80, 96, 256];

  for (const size of targetSizes) {
    for (const variant of ["unplated", "lightunplated"]) {
      const name = `Square44x44Logo.targetsize-${size}_altform-${variant}.png`;
      assert.ok(assetNames.has(name), `missing ${name}`);

      const png = readFileSync(new URL(name, msixAssetsUrl));
      assert.equal(png.readUInt32BE(16), size, `${name} width`);
      assert.equal(png.readUInt32BE(20), size, `${name} height`);
    }
  }
});

test("MSIX packaging indexes qualified icon assets in resources.pri", () => {
  assert.match(packageScript, /Find-WindowsSdkTool "makepri\.exe"/);
  assert.match(packageScript, /createconfig/);
  assert.match(packageScript, /makePri[\s\S]*?\bnew\b/);
  assert.match(packageScript, /resources\.pri/);
});
