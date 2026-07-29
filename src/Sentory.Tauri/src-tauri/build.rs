fn main() {
    let windows = tauri_build::WindowsAttributes::new()
        .window_icon_path("../../Sentory.App/Assets/Sentory.ico");
    let attributes = tauri_build::Attributes::new()
        .windows_attributes(windows);

    tauri_build::try_build(attributes)
        .expect("failed to run the Sentory Tauri build script");
}
