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
    vocal_fixture(Path::new(&args[1]), Path::new(&args[2]))?;
    Ok(())
}

fn vlq(mut value: u32, bytes: &mut Vec<u8>) {
    let mut buffer = [0u8; 5];
    let mut i = 4;
    buffer[i] = (value & 127) as u8;
    while {
        value >>= 7;
        value != 0
    } {
        i -= 1;
        buffer[i] = (value as u8 & 127) | 128;
    }
    bytes.extend_from_slice(&buffer[i..]);
}

fn meta(kind: u8, value: &[u8]) -> Vec<u8> {
    let mut bytes = vec![255, kind];
    vlq(value.len() as u32, &mut bytes);
    bytes.extend(value);
    bytes
}

fn vocal_fixture(output: &Path, audio: &Path) -> Result<(), Box<dyn Error>> {
    let dir = output.join("Venue Vocal Dynamics");
    fs::create_dir_all(&dir)?;
    let mut tracks = Vec::new();
    tracks.push(vec![
        (0, meta(3, b"TEMPO")),
        (0, meta(81, &[7, 161, 32])),
        (7680, meta(81, &[5, 22, 21])),
        (30720, meta(81, &[11, 113, 176])),
    ]);
    tracks.push(vec![
        (0, meta(3, b"EVENTS")),
        (0, meta(1, b"[section intro]")),
        (3840, meta(1, b"[section verse]")),
        (13440, meta(1, b"[section chorus]")),
        (30720, meta(1, b"[section outro]")),
        (44160, meta(1, b"[end]")),
    ]);
    for name in ["PART GUITAR", "PART BASS", "PART DRUMS", "PART VOCALS"] {
        let mut events = vec![(0, meta(3, name.as_bytes()))];
        for (start, end, step) in [(3840, 7680, 480), (13440, 24960, 480), (30720, 36480, 480)] {
            if name == "PART VOCALS" {
                events.push((start, vec![144, 105, 100]));
            }
            for (i, tick) in (start..end).step_by(step).enumerate() {
                let note = if name == "PART VOCALS" {
                    60 + (i % 3) as u8
                } else if name == "PART DRUMS" {
                    96 + (i % 2) as u8
                } else {
                    96 + (i % 5) as u8
                };
                if name == "PART VOCALS" {
                    events.push((tick, meta(5, b"ah")));
                }
                events.push((tick, vec![144, note, 100]));
                events.push((tick + 360, vec![128, note, 0]));
            }
            if name == "PART VOCALS" {
                events.push((end, vec![128, 105, 0]));
            }
        }
        tracks.push(events);
    }
    let mut midi = b"MThd".to_vec();
    midi.extend(6u32.to_be_bytes());
    midi.extend(1u16.to_be_bytes());
    midi.extend((tracks.len() as u16).to_be_bytes());
    midi.extend(480u16.to_be_bytes());
    for mut events in tracks {
        events.sort_by_key(|(tick, _)| *tick);
        let mut data = Vec::new();
        let mut previous = 0;
        for (tick, event) in events {
            vlq(tick - previous, &mut data);
            data.extend(event);
            previous = tick;
        }
        vlq(44160 - previous, &mut data);
        data.extend([255, 47, 0]);
        midi.extend(b"MTrk");
        midi.extend((data.len() as u32).to_be_bytes());
        midi.extend(data);
    }
    fs::write(dir.join("notes.mid"), midi)?;
    fs::write(dir.join("song.ini"),"[song]\nname = Venue Vocal Dynamics\nartist = Controlled venue regression\ncharter = Local fixture\nsong_length = 45000\n")?;
    fs::copy(audio, dir.join("song.ogg"))?;
    println!("{}", dir.display());
    Ok(())
}
