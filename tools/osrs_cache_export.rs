//! Bounded, read-only local OSRS cache extraction. No game data is redistributed.
//! Usage: osrs_cache_export CACHE_DIRECTORY OUTPUT_DIRECTORY NPC_ID...
//! Mesh/definition layouts follow RuneLite's ModelLoader and NpcLoader.
//! See LICENSE-runelite-cache.txt for attribution and redistribution terms.
use std::{
    collections::BTreeMap,
    error::Error,
    fs::{self, File},
    io::{Read, Seek, SeekFrom, Write},
    path::{Path, PathBuf},
    process::{Command, Stdio},
};
type Result<T> = std::result::Result<T, Box<dyn Error>>;
struct Cursor<'a> {
    b: &'a [u8],
    p: usize,
}
impl<'a> Cursor<'a> {
    fn new(b: &'a [u8], p: usize) -> Self {
        Self { b, p }
    }
    fn take(&mut self, n: usize) -> Result<&'a [u8]> {
        let s = self
            .b
            .get(self.p..self.p.checked_add(n).ok_or("overflow")?)
            .ok_or("truncated data")?;
        self.p += n;
        Ok(s)
    }
    fn u8(&mut self) -> Result<usize> {
        Ok(self.take(1)?[0] as usize)
    }
    fn u16(&mut self) -> Result<usize> {
        let b = self.take(2)?;
        Ok(u16::from_be_bytes([b[0], b[1]]) as usize)
    }
    fn u24(&mut self) -> Result<usize> {
        let b = self.take(3)?;
        Ok(((b[0] as usize) << 16) | ((b[1] as usize) << 8) | b[2] as usize)
    }
    fn u32(&mut self) -> Result<usize> {
        let b = self.take(4)?;
        Ok(u32::from_be_bytes(b.try_into()?) as usize)
    }
    fn smart(&mut self) -> Result<i32> {
        if *self.b.get(self.p).ok_or("smart EOF")? < 128 {
            Ok(self.u8()? as i32 - 64)
        } else {
            Ok(self.u16()? as i32 - 49152)
        }
    }
    fn big(&mut self) -> Result<usize> {
        if *self.b.get(self.p).ok_or("big EOF")? < 128 {
            self.u16()
        } else {
            Ok(self.u32()? & 0x7fffffff)
        }
    }
    fn string(&mut self) -> Result<String> {
        let start = self.p;
        while self.u8()? != 0 {}
        Ok(String::from_utf8_lossy(&self.b[start..self.p - 1]).to_string())
    }
}
fn decompress(b: &[u8]) -> Result<Vec<u8>> {
    let mut c = Cursor::new(b, 0);
    let kind = c.u8()?;
    let size = c.u32()?;
    if size > 16 * 1024 * 1024 {
        return Err("compressed archive exceeds bound".into());
    }
    if kind == 0 {
        return Ok(c.take(size)?.to_vec());
    }
    let expanded = c.u32()?;
    if expanded > 32 * 1024 * 1024 {
        return Err("expanded archive exceeds bound".into());
    }
    let data = c.take(size)?;
    let program = match kind {
        1 => "/usr/bin/bzip2",
        2 => "/usr/bin/gzip",
        _ => return Err("unsupported archive compression".into()),
    };
    let mut child = Command::new(program)
        .arg("-dc")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()?;
    let mut input = child.stdin.take().ok_or("missing stdin")?;
    let mut bytes = Vec::new();
    if kind == 1 {
        bytes.extend_from_slice(b"BZh1");
    }
    bytes.extend_from_slice(data);
    // Drain stdout concurrently with writing so pipe capacity cannot deadlock.
    let writer = std::thread::spawn(move || input.write_all(&bytes));
    let output = child.wait_with_output()?;
    writer.join().map_err(|_| "decompress writer panic")??;
    if !output.status.success() || output.stdout.len() != expanded {
        return Err("archive decompression failed or wrong length".into());
    }
    Ok(output.stdout)
}
struct Cache {
    dir: PathBuf,
    data: File,
}
impl Cache {
    fn new(dir: &Path) -> Result<Self> {
        Ok(Self {
            dir: dir.into(),
            data: File::open(dir.join("main_file_cache.dat2"))?,
        })
    }
    fn archive(&mut self, index: usize, id: usize) -> Result<Vec<u8>> {
        let mut file = File::open(self.dir.join(format!("main_file_cache.idx{index}")))?;
        file.seek(SeekFrom::Start((id * 6) as u64))?;
        let mut entry = [0; 6];
        file.read_exact(&mut entry)?;
        let mut c = Cursor::new(&entry, 0);
        let size = c.u24()?;
        let mut sector = c.u24()?;
        if size == 0 || size > 16 * 1024 * 1024 {
            return Err(format!("missing/oversized archive {index}/{id}").into());
        }
        let mut bytes = Vec::with_capacity(size);
        let mut part = 0;
        while bytes.len() < size {
            if sector == 0 || part > 40000 {
                return Err("invalid cache sector chain".into());
            }
            self.data.seek(SeekFrom::Start((sector * 520) as u64))?;
            let header = if id > 65535 { 10 } else { 8 };
            let mut b = vec![0; header + (size - bytes.len()).min(520 - header)];
            self.data.read_exact(&mut b)?;
            let mut c = Cursor::new(&b, 0);
            let stored_id = if id > 65535 { c.u32()? } else { c.u16()? };
            let stored_part = c.u16()?;
            sector = c.u24()?;
            let stored_index = c.u8()?;
            if stored_id != id || stored_part != part || stored_index != index {
                return Err("cache sector ownership mismatch".into());
            }
            bytes.extend_from_slice(&b[header..]);
            part += 1;
        }
        decompress(&bytes)
    }
}
fn file_ids(b: &[u8], wanted: usize) -> Result<Vec<usize>> {
    let mut c = Cursor::new(b, 0);
    let version = c.u8()?;
    if !(5..=7).contains(&version) {
        return Err("unsupported index version".into());
    }
    if version >= 6 {
        c.u32()?;
    }
    let flags = c.u8()?;
    if flags & !15 != 0 {
        return Err("unsupported index flags".into());
    }
    let count = if version >= 7 { c.big()? } else { c.u16()? };
    if count > 100000 {
        return Err("index count exceeds bound".into());
    }
    let mut archives = Vec::new();
    let mut id = 0;
    for _ in 0..count {
        id += if version >= 7 { c.big()? } else { c.u16()? };
        archives.push(id);
    }
    if flags & 1 != 0 {
        c.take(count * 4)?;
    }
    c.take(count * 4)?; // compressed CRCs
    if flags & 8 != 0 {
        c.take(count * 4)?;
    }
    if flags & 2 != 0 {
        c.take(count * 64)?;
    }
    if flags & 4 != 0 {
        c.take(count * 8)?;
    }
    c.take(count * 4)?; // versions
    let mut counts = Vec::new();
    for _ in 0..count {
        counts.push(if version >= 7 { c.big()? } else { c.u16()? });
    }
    for (archive, n) in archives.into_iter().zip(counts) {
        let mut ids = Vec::new();
        let mut id = 0;
        for _ in 0..n {
            id += if version >= 7 { c.big()? } else { c.u16()? };
            ids.push(id);
        }
        if archive == wanted {
            return Ok(ids);
        }
    }
    Err("archive absent from index".into())
}
fn split_files(b: &[u8], n: usize) -> Result<Vec<Vec<u8>>> {
    if n == 1 {
        return Ok(vec![b.to_vec()]);
    }
    if n == 0 {
        return Err("empty archive".into());
    }
    let chunks = *b.last().ok_or("empty archive data")? as usize;
    let table = b
        .len()
        .checked_sub(1 + chunks * n * 4)
        .ok_or("truncated chunk table")?;
    let mut cursor = Cursor::new(b, table);
    let mut files = vec![Vec::new(); n];
    let mut offset = 0;
    for _ in 0..chunks {
        let mut length = 0i64;
        for file in &mut files {
            length += cursor.u32()? as u32 as i32 as i64;
            if length < 0 {
                return Err("negative chunk size".into());
            }
            let end = offset + length as usize;
            if end > table {
                return Err("chunk overlaps table".into());
            }
            file.extend_from_slice(&b[offset..end]);
            offset = end;
        }
    }
    if offset != table {
        return Err("chunk data mismatch".into());
    }
    Ok(files)
}
struct Npc {
    name: String,
    models: Vec<usize>,
    recolors: BTreeMap<usize, usize>,
}
fn npc(b: &[u8]) -> Result<Npc> {
    let mut c = Cursor::new(b, 0);
    let mut n = Npc {
        name: String::new(),
        models: vec![],
        recolors: BTreeMap::new(),
    };
    loop {
        match c.u8()? {
            0 => break,
            1 => {
                let count = c.u8()?;
                for _ in 0..count {
                    n.models.push(c.u16()?);
                }
            }
            2 => n.name = c.string()?,
            12 | 100 | 101 => {
                c.u8()?;
            }
            13..=16 | 18 | 74..=79 | 95 | 97 | 98 | 103 | 114 | 116 | 124 => {
                c.u16()?;
            }
            17 | 115 | 117 => {
                c.take(8)?;
            }
            30..=34 => {
                c.string()?;
            }
            40 => {
                let count = c.u8()?;
                for _ in 0..count {
                    n.recolors.insert(c.u16()?, c.u16()?);
                }
            }
            41 => {
                let count = c.u8()?;
                c.take(count * 4)?;
            }
            60 => {
                let count = c.u8()?;
                c.take(count * 2)?;
            }
            61 => {
                let count = c.u8()?;
                for _ in 0..count {
                    n.models.push(c.u32()?);
                }
            }
            62 => {
                let count = c.u8()?;
                c.take(count * 4)?;
            }

            93 | 99 | 107 | 109 | 111 | 122 | 123 => {}
            251 => {
                c.take(2)?;
                c.string()?;
            }
            252 => {
                c.take(13)?;
                c.string()?;
            }
            253 => {
                c.take(15)?;
                c.string()?;
            }

            249 => {
                let count = c.u8()?;
                for _ in 0..count {
                    let text = c.u8()?;
                    c.u24()?;
                    if text == 1 {
                        c.string()?;
                    } else {
                        c.u32()?;
                    }
                }
            }
            op => {
                return Err(format!(
                    "NPC {} models {:?} at {} opcode {op} needs an explicit decoder",
                    n.name, n.models, c.p
                )
                .into())
            }
        }
    }
    if n.name.is_empty() || n.models.is_empty() {
        return Err("NPC has no named body".into());
    }
    Ok(n)
}
struct Mesh {
    p: Vec<[i32; 3]>,
    groups: Vec<usize>,
    f: Vec<[usize; 3]>,
    colors: Vec<usize>,
}
fn model(b: &[u8]) -> Result<Mesh> {
    if b.len() < 18 {
        return Err("truncated mesh".into());
    }
    let kind = if b[b.len() - 2] == 255 {
        b[b.len() - 1]
    } else {
        0
    };
    if ![0, 253, 254, 255].contains(&kind) {
        return Err(format!("mesh format {kind} is not yet supported").into());
    }
    let modern = kind == 253 || kind == 255;
    let mut footer = Cursor::new(
        b,
        b.len()
            .checked_sub(match kind {
                253 => 26,
                254 | 255 => 23,
                _ => 18,
            })
            .ok_or("truncated mesh footer")?,
    );
    let nv = footer.u16()?;
    let nf = footer.u16()?;
    let nt = footer.u8()?;
    let texture = footer.u8()?;
    let priority = footer.u8()?;
    let alpha = footer.u8()?;
    let face_groups = footer.u8()?;
    let face_textures = if modern { footer.u8()? } else { 0 };
    let vertex_groups = footer.u8()?;
    if kind == 254 || kind == 253 {
        footer.u8()?;
    }
    let nx = footer.u16()?;
    let ny = footer.u16()?;
    let _nz = footer.u16()?;
    let ni = footer.u16()?;
    let coord_bytes = if modern { footer.u16()? } else { 0 };
    let ng = if kind == 254 || kind == 253 {
        footer.u16()?
    } else {
        nv * vertex_groups
    };
    let mut o = if modern {
        nt + nv + if texture == 1 { nf } else { 0 }
    } else {
        nv
    };
    let types = o;
    o += nf;
    if priority == 255 {
        o += nf;
    }
    if face_groups == 1 {
        o += nf;
    }
    let texture_flags = o;
    if !modern && texture == 1 {
        o += nf;
    }
    let group_offset = o;
    o += ng;
    let alpha_flags = o;
    if alpha == 1 {
        o += nf;
    }
    let indices = o;
    o += ni;
    let texture_ids = o;
    if face_textures == 1 {
        o += nf * 2;
    }
    o += coord_bytes;
    let colors = o;
    o += nf * 2 + if modern { 0 } else { nt * 6 };
    let (mut x, mut y, mut z) = (
        Cursor::new(b, o),
        Cursor::new(b, o + nx),
        Cursor::new(b, o + nx + ny),
    );
    let mut flags = Cursor::new(b, if modern { nt } else { 0 });
    let mut previous = [0; 3];
    let mut p = Vec::new();
    for _ in 0..nv {
        let f = flags.u8()?;
        for (a, c) in [&mut x, &mut y, &mut z].iter_mut().enumerate() {
            if f & (1 << a) != 0 {
                previous[a] += c.smart()?;
            }
        }
        p.push(previous);
    }
    let mut c = Cursor::new(b, colors);
    let mut color = Vec::new();
    for _ in 0..nf {
        color.push(c.u16()?);
    }
    let mut c = Cursor::new(b, indices);
    let mut t = Cursor::new(b, types);
    let (mut a, mut d, mut e, mut last) = (0i32, 0i32, 0i32, 0i32);
    let mut faces = Vec::new();
    for i in 0..nf {
        match t.u8()? {
            1 => {
                a = c.smart()? + last;
                d = c.smart()? + a;
                e = c.smart()? + d;
                last = e;
            }
            2 => {
                d = e;
                e = c.smart()? + last;
                last = e;
            }
            3 => {
                a = e;
                e = c.smart()? + last;
                last = e;
            }
            4 => {
                std::mem::swap(&mut a, &mut d);
                e = c.smart()? + last;
                last = e;
            }
            _ => return Err("invalid triangle compression".into()),
        }
        if [a, d, e].iter().any(|v| *v < 0 || *v as usize >= nv) {
            return Err("triangle index outside mesh".into());
        }
        if !modern && texture == 1 && b.get(texture_flags + i).ok_or("texture EOF")? & 2 != 0 {
            return Err("textured mesh requires texture decoding".into());
        }
        if face_textures == 1 && Cursor::new(b, texture_ids + i * 2).u16()? != 0 {
            return Err("textured mesh requires texture decoding".into());
        }
        if alpha == 1 && b.get(alpha_flags + i) == Some(&255) {
            continue;
        }
        faces.push(([a as usize, d as usize, e as usize], color[i]));
    }
    Ok(Mesh {
        groups: if vertex_groups == 1 {
            b.get(group_offset..group_offset + nv)
                .ok_or("group EOF")?
                .iter()
                .map(|v| *v as usize)
                .collect()
        } else {
            return Err("NPC part has no legacy body groups".into());
        },
        p,
        f: faces.iter().map(|f| f.0).collect(),
        colors: faces.iter().map(|f| f.1).collect(),
    })
}
fn palette(path: &Path) -> Result<()> {
    let mut image = vec![0u8; 18];
    image[2] = 2;
    image[12..14].copy_from_slice(&256u16.to_le_bytes());
    image[14..16].copy_from_slice(&256u16.to_le_bytes());
    image[16] = 24;
    for i in 0..65536 {
        let h = (i >> 10) as f32 / 64.0 + 1.0 / 128.0;
        let s = ((i >> 7) & 7) as f32 / 8.0 + 1.0 / 16.0;
        let l = (i & 127) as f32 / 128.0;
        let q = if l < 0.5 {
            l * (1.0 + s)
        } else {
            l + s - l * s
        };
        let p = 2.0 * l - q;
        let hue = |mut t: f32| {
            if t < 0.0 {
                t += 1.0;
            }
            if t > 1.0 {
                t -= 1.0;
            }
            let v = if t < 1.0 / 6.0 {
                p + (q - p) * 6.0 * t
            } else if t < 0.5 {
                q
            } else if t < 2.0 / 3.0 {
                p + (q - p) * (2.0 / 3.0 - t) * 6.0
            } else {
                p
            };
            (v.clamp(0.0, 1.0).powf(0.8) * 255.0).round() as u8
        };
        image.extend_from_slice(&[hue(h - 1.0 / 3.0), hue(h), hue(h + 1.0 / 3.0)]);
    }
    fs::write(path, image)?;
    Ok(())
}
fn main() -> Result<()> {
    let a: Vec<_> = std::env::args().collect();
    if a.len() < 4 || a.len() > 12 {
        return Err("expected CACHE OUTPUT and 1..9 NPC IDs".into());
    }
    let cache_dir = Path::new(&a[1]);
    let output = Path::new(&a[2]);
    fs::create_dir_all(output)?;
    let before = fs::metadata(cache_dir.join("main_file_cache.dat2"))?.modified()?;
    let mut cache = Cache::new(cache_dir)?;
    let ids = file_ids(&cache.archive(255, 2)?, 9)?;
    let files = split_files(&cache.archive(2, 9)?, ids.len())?;
    for text in &a[3..] {
        let id: usize = text.parse()?;
        let index = ids.iter().position(|x| *x == id).ok_or("NPC absent")?;
        let npc = npc(&files[index])?;
        println!("NPC {id}: {} models={:?}", npc.name, npc.models);
        let mut meshes = Vec::new();
        for &model_id in &npc.models {
            let b = cache.archive(7, model_id)?;
            println!(
                "model {model_id}: {} bytes footer={:?}",
                b.len(),
                b.get(b.len().saturating_sub(2)..).unwrap_or_default()
            );
            meshes.push(model(&b)?);
        }
        let dir = output.join(format!("{}-{id}", npc.name.replace(' ', "_")));
        fs::create_dir_all(&dir)?;

        // Unity interchange preserves the cache's original groups and authored rig scale.
        // Raw cache coordinates use Y down; this matches the imported FBX rest pose.
        let mut positions = Vec::new();
        let mut uvs = Vec::new();
        let mut bone_groups = Vec::new();
        for m in &meshes {
            for (face, color) in m.f.iter().zip(&m.colors) {
                let color = *npc.recolors.get(color).unwrap_or(color);
                for &i in face.iter().rev() {
                    let p = m.p[i];
                    positions.push([
                        -(p[0] as f32) * 0.01,
                        -(p[1] as f32) * 0.01,
                        -(p[2] as f32) * 0.01,
                    ]);
                    uvs.push([
                        ((color % 256) as f32 + 0.5) / 256.,
                        ((color / 256) as f32 + 0.5) / 256.,
                    ]);
                    bone_groups.push(m.groups[i]);
                }
            }
        }
        fs::write(
            dir.join("source-mesh.json"),
            format!("{{\"positions\":{positions:?},\"uvs\":{uvs:?},\"groups\":{bone_groups:?}}}"),
        )?;
        palette(&dir.join("palette.tga"))?;
        fs::write(dir.join("SOURCE.txt"),format!("Read-only local RuneLite cache extraction. NPC {}: {}. Models {:?}. No game geometry may be committed to the fork.\n",id,npc.name,npc.models))?;
        println!("EXPORTED {}", dir.display());
    }
    if fs::metadata(cache_dir.join("main_file_cache.dat2"))?.modified()? != before {
        return Err("cache changed during export; rerun with client closed".into());
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn archive_chunk_deltas_and_truncation() {
        let mut bytes = b"ab123c45".to_vec();
        for delta in [2i32, 1, 1, 1] {
            bytes.extend(delta.to_be_bytes());
        }
        bytes.push(2);
        assert_eq!(
            split_files(&bytes, 2).unwrap(),
            vec![b"abc".to_vec(), b"12345".to_vec()]
        );
        assert!(split_files(&bytes[..5], 2).is_err());
        assert!(Cursor::new(&[128], 0).smart().is_err());
    }
    #[test]
    fn legacy_and_modern_meshes_keep_original_vertex_groups() {
        for kind in [0, 254, 253] {
            let mut bytes = vec![0, 1, 3, 1, 0, 17, 27, 64, 65, 65, 0x12, 0x34, 65, 63, 65];
            bytes.extend([0, 3, 0, 1, 0, 0, 0, 0, 0]);
            if kind == 253 {
                bytes.push(0);
            }
            bytes.push(1);
            if kind != 0 {
                bytes.push(0);
            }
            bytes.extend([0, 2, 0, 1, 0, 0, 0, 3]);
            if kind == 253 {
                bytes.extend([0, 0]);
            }
            if kind != 0 {
                bytes.extend([0, 3, 255, kind]);
            }
            let mesh = model(&bytes).unwrap();
            assert_eq!(mesh.p, vec![[0, 0, 0], [1, 0, 0], [0, 1, 0]]);
            assert_eq!(mesh.groups, vec![0, 17, 27]);
            assert_eq!(mesh.f, vec![[0, 1, 2]]);
            assert_eq!(mesh.colors, vec![0x1234]);
        }
        assert!(model(&[255; 18]).is_err());
    }
    #[test]
    fn current_npc_definition_uses_full_model_ids() {
        let mut bytes = vec![61, 1];
        bytes.extend(217246u32.to_be_bytes());
        bytes.extend(b"\x02Hans\0\xfb\0\0Talk-to\0\0");
        let parsed = npc(&bytes).unwrap();
        assert_eq!(parsed.name, "Hans");
        assert_eq!(parsed.models, vec![217246]);
        assert!(npc(&bytes[..6]).is_err());
    }
}
