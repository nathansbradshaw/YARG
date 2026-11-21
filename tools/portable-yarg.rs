use std::{env, fs, io, path::{Path, PathBuf}, process::Command};
use std::os::unix::process::CommandExt;

fn json_text(s: &str) -> String {
    s.chars().flat_map(|c| match c {
        '"' => "\\\"".chars().collect::<Vec<_>>(),
        '\\' => "\\\\".chars().collect(),
        c if c.is_control() => format!("\\u{:04x}", c as u32).chars().collect(),
        c => vec![c],
    }).collect()
}

fn paths(exe: &Path) -> io::Result<(PathBuf, PathBuf, PathBuf)> {
    let install = exe.ancestors().nth(4).ok_or_else(|| io::Error::other("Invalid portable app layout"))?;
    let volume = install.parent().ok_or_else(|| io::Error::other("Missing volume"))?;
    Ok((install.to_owned(), install.join("Data"), volume.to_owned()))
}

fn run() -> io::Result<()> {
    let exe = env::current_exe()?;
    let (install, data, volume) = paths(&exe)?;
    fs::create_dir_all(install.join("Logs"))?;
    let settings = data.join("settings.json");
    let marker = data.join("last-volume-path.txt");
    let current = volume.to_string_lossy();
    let old = fs::read_to_string(&marker)?;
    if old != current {
        // Relocate volume-prefixed settings paths, leaving unrelated preferences alone.
        let content = fs::read_to_string(&settings)?;
        let relocated = content.replace(&format!("\"{}/", json_text(&old)), &format!("\"{}/", json_text(&current)));
        fs::write(data.join("settings.before-relocation.json"), &content)?;
        fs::write(&settings, relocated)?;
        fs::write(&marker, current.as_bytes())?;
    }
    if env::args().any(|a| a == "--portable-check") {
        println!("App: {}\nData: {}\nLibrary: {}", exe.display(), data.display(), volume.join("Library").display());
        return Ok(());
    }
    let error = Command::new(exe.with_file_name("YARG-Player"))
        .args(env::args_os().skip(1))
        .arg("-persistent-data-path").arg(&data)
        .arg("-download-location").arg(&install)
        .arg("-logFile").arg(install.join("Logs/Player.log"))
        .current_dir(&install).exec();
    Err(error)
}

fn main() {
    if let Err(e) = run() { eprintln!("Portable YARG: {e}"); std::process::exit(1); }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn relocated_volume_with_spaces() {
        let (install, data, volume)=paths(Path::new("/Volumes/My Games/YARG/YARG.app/Contents/MacOS/YARG")).unwrap();
        assert_eq!(install,Path::new("/Volumes/My Games/YARG"));
        assert_eq!(data,install.join("Data"));
        assert_eq!(volume,Path::new("/Volumes/My Games"));
        assert_eq!(json_text("drive\"\\name"),"drive\\\"\\\\name");
    }
}
