//! Generate controlled YARG charts for checking band timing and missing-part behavior.
//! Usage: rustc tools/venue_fixture_generator.rs -o /tmp/yarg-venue-fixtures
//!        /tmp/yarg-venue-fixtures OUTPUT_DIRECTORY AUDIO_45_SECONDS.ogg
use std::{env, error::Error, fmt::Write as _, fs, path::Path};

fn main() -> Result<(), Box<dyn Error>> {
    let args: Vec<_> = env::args().collect();
    if args.len() != 3 {
        return Err("expected output directory and a 45-second Ogg audio file".into());
    }
    for (name, difficulty, full_band) in [
        ("Venue Dynamics", "Expert", true),
        ("Venue Hard Only", "Hard", true),
        ("Venue Guitar Only", "Expert", false),
    ] {
        let dir = Path::new(&args[1]).join(name);
        fs::create_dir_all(&dir)?;
        let mut chart = format!("[Song]\n{{\n Name = \"{name}\"\n Artist = \"Controlled venue regression\"\n Resolution = 192\n MusicStream = \"song.ogg\"\n}}\n[SyncTrack]\n{{\n 0 = B 120000\n 3072 = B 180000\n 12288 = B 80000\n}}\n[Events]\n{{\n 0 = E \"section intro\"\n 1536 = E \"section verse\"\n 3072 = E \"section break\"\n 5376 = E \"section chorus\"\n 9984 = E \"section break\"\n 12288 = E \"section outro\"\n}}\n");
        for part in if full_band {
            vec!["Single", "DoubleBass", "Drums"]
        } else {
            vec!["Single"]
        } {
            writeln!(chart, "[{difficulty}{part}]\n{{")?;
            // 4–8 s at 120 BPM, 12–20 s at 180 BPM, then 24–33 s at 80 BPM.
            // Silent gaps are deliberate; the audio is calibration music, not a musical chart.
            for (start, end, step) in [(1536, 3072, 96), (5376, 9984, 96), (12288, 14592, 192)] {
                for (index, tick) in (start..end).step_by(step).enumerate() {
                    let lane = if part == "Drums" {
                        if index % 2 == 0 {
                            0
                        } else {
                            1
                        }
                    } else {
                        index % 5
                    };
                    writeln!(chart, " {tick} = N {lane} 0")?;
                }
            }
            writeln!(chart, "}}")?;
        }
        fs::write(dir.join("notes.chart"), chart)?;
        fs::write(dir.join("song.ini"), format!("[song]\nname = {name}\nartist = Controlled venue regression\ncharter = Local fixture\nsong_length = 45000\n"))?;
        fs::copy(&args[2], dir.join("song.ogg"))?;
        println!("{}", dir.display());
    }
    Ok(())
}
