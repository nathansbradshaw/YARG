//! Build with `rustc Tools/build-dance.rs -o /tmp/yarg-build-dance`.
//! Run from the project root. Uses a single shared native Cargo target directory.
use std::{
    env, fs,
    path::{Path, PathBuf},
    process::{Command, ExitCode},
};
fn run(command: &mut Command) -> Result<(), String> {
    if command.status().map_err(|e| e.to_string())?.success() {
        Ok(())
    } else {
        Err(format!("Command failed: {command:?}"))
    }
}
fn build(root: &Path, target: &str) -> Result<PathBuf, String> {
    let manifest = root.join("YARG.Core/Native/dance/Cargo.toml");
    run(Command::new("cargo")
        .args([
            "build",
            "--release",
            "--locked",
            "-j",
            "2",
            "--target",
            target,
            "--manifest-path",
        ])
        .arg(manifest))?;
    Ok(root
        .join("YARG.Core/Native/dance/target")
        .join(target)
        .join("release"))
}
fn main() -> ExitCode {
    let result = (|| -> Result<(), String> {
        let root = env::current_dir().map_err(|e| e.to_string())?;
        let destination = root.join("Assets/Plugins/Dance");
        fs::create_dir_all(&destination).map_err(|e| e.to_string())?;
        let platform = env::args().nth(1).unwrap_or(env::consts::OS.into());
        let (file, guid, os, target) = match platform.as_str() {
            "macos" => (
                "libyarg_dance.dylib",
                "7f68868cdb2d456091d70f387a7ba16e",
                "OSX",
                "OSXUniversal",
            ),
            "linux" => (
                "libyarg_dance.so",
                "26da940d08b94fd6887f3b7b190c8aaf",
                "Linux",
                "Linux64",
            ),
            "windows" => (
                "yarg_dance.dll",
                "5fd3d9a22f614ed99c923c8d52298187",
                "Windows",
                "Win64",
            ),
            _ => return Err("Supported platforms: macos, linux, windows".into()),
        };
        if platform == "macos" {
            let arm = build(&root, "aarch64-apple-darwin")?.join(file);
            let intel = build(&root, "x86_64-apple-darwin")?.join(file);
            run(Command::new("lipo")
                .args(["-create"])
                .arg(arm)
                .arg(intel)
                .arg("-output")
                .arg(destination.join(file)))?;
        } else {
            let triple = if platform == "linux" {
                "x86_64-unknown-linux-gnu"
            } else {
                "x86_64-pc-windows-msvc"
            };
            let source = build(&root, triple)?.join(file);
            fs::copy(source, destination.join(file)).map_err(|e| e.to_string())?;
        }
        let meta=format!("fileFormatVersion: 2\nguid: {guid}\nPluginImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n  iconMap: {{}}\n  executionOrder: {{}}\n  defineConstraints: []\n  isPreloaded: 0\n  isOverridable: 0\n  isExplicitlyReferenced: 0\n  validateReferences: 1\n  platformData:\n  - first:\n      Any: \n    second:\n      enabled: 0\n      settings: {{}}\n  - first:\n      Editor: Editor\n    second:\n      enabled: 1\n      settings:\n        CPU: {}\n        DefaultValueInitialized: true\n        OS: {os}\n  - first:\n      Standalone: {target}\n    second:\n      enabled: 1\n      settings:\n        CPU: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",if platform=="macos" {"AnyCPU"} else {"x86_64"},if platform=="macos" {"AnyCPU"} else {"x86_64"});
        fs::write(destination.join(format!("{file}.meta")), meta).map_err(|e| e.to_string())?;
        println!(
            "Built Dance native library: {}",
            destination.join(file).display()
        );
        Ok(())
    })();
    match result {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            eprintln!("{error}");
            ExitCode::FAILURE
        }
    }
}
