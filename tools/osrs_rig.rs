//! XPS ASCII mesh to editable Unity rig input. No external dependencies.
//! Usage: osrs_rig INPUT.mesh.ascii OUTPUT_DIRECTORY
use std::{env, error::Error, fmt::Write as _, fs, path::Path};

fn values<T: std::str::FromStr>(line: &str) -> Vec<T> {
    line.split_whitespace()
        .map(|x| x.parse().ok().expect("invalid number"))
        .collect()
}

fn arm_pose(p: &[f32], side: f32) -> Vec<f32> {
    vec![
        side * 0.235 - side * (p[1] - 1.385),
        1.385 + side * (p[0] - side * 0.235),
        p[2],
    ]
}

fn skeleton() -> Vec<(&'static str, i32, Vec<f32>)> {
    let mut b = vec![
        ("Hips", -1, vec![0., 0.90, 0.]),
        ("Spine", 0, vec![0., 1.06, 0.]),
        ("Chest", 1, vec![0., 1.27, 0.]),
        ("UpperChest", 2, vec![0., 1.39, 0.]),
        ("Neck", 3, vec![0., 1.47, 0.]),
        ("Head", 4, vec![0., 1.56, 0.]),
        ("LeftShoulder", 3, vec![0.15, 1.385, 0.]),
        ("LeftUpperArm", 6, vec![0.235, 1.385, 0.]),
        ("LeftLowerArm", 7, vec![0.265, 1.12, 0.]),
        ("LeftHand", 8, vec![0.265, 0.865, 0.]),
        ("RightShoulder", 3, vec![-0.15, 1.385, 0.]),
        ("RightUpperArm", 10, vec![-0.235, 1.385, 0.]),
        ("RightLowerArm", 11, vec![-0.265, 1.12, 0.]),
        ("RightHand", 12, vec![-0.265, 0.865, 0.]),
        ("LeftUpperLeg", 0, vec![0.09, 0.88, 0.]),
        ("LeftLowerLeg", 14, vec![0.09, 0.46, 0.]),
        ("LeftFoot", 15, vec![0.09, 0.09, 0.025]),
        ("LeftToes", 16, vec![0.09, 0.035, 0.14]),
        ("RightUpperLeg", 0, vec![-0.09, 0.88, 0.]),
        ("RightLowerLeg", 18, vec![-0.09, 0.46, 0.]),
        ("RightFoot", 19, vec![-0.09, 0.09, 0.025]),
        ("RightToes", 20, vec![-0.09, 0.035, 0.14]),
    ];
    for i in [7, 8, 9, 11, 12, 13] {
        b[i].2 = arm_pose(&b[i].2, if i < 10 { 1. } else { -1. });
    }
    b
}

fn skin(p: &[f32]) -> (Vec<usize>, Vec<f32>, f32) {
    let (x, y) = (p[0], p[1]);
    let body = if y > 1.49 {
        5
    } else if y > 1.43 {
        4
    } else if y > 1.34 {
        3
    } else if y > 1.17 {
        2
    } else if y > 0.96 {
        1
    } else {
        0
    };
    if y < 0.83 {
        let leg = if x > 0. { 14 } else { 18 };
        let (a, b, t) = if y < 0.17 {
            (leg + 2, leg + 2, 0.)
        } else {
            (leg + 1, leg, ((y - 0.42) / 0.08).clamp(0., 1.))
        };
        return (vec![a, b, 0, 0], vec![1. - t, t, 0., 0.], 0.);
    }
    let arm = ((x.abs() - 0.165) / 0.065).clamp(0., 1.) * ((1.48 - y) / 0.07).clamp(0., 1.);
    let base = if x > 0. { 7 } else { 11 };
    let (a, b, t) = if y < 1.04 {
        (base + 2, base + 1, ((y - 0.91) / 0.06).clamp(0., 1.))
    } else {
        (base + 1, base, ((y - 1.18) / 0.06).clamp(0., 1.))
    };
    (
        vec![body, a, b, 0],
        vec![1. - arm, arm * (1. - t), arm * t, 0.],
        arm,
    )
}

fn main() -> Result<(), Box<dyn Error>> {
    let args: Vec<_> = env::args().collect();
    if args.len() != 3 {
        return Err("expected XPS ASCII input and output directory".into());
    }
    let input = fs::read_to_string(&args[1])?;
    let mut lines = input.lines();
    let bones: usize = lines.next().ok_or("missing bones")?.parse()?;
    for _ in 0..bones {
        for _ in 0..3 {
            lines.next().ok_or("incomplete skeleton")?;
        }
    }
    let meshes: usize = lines.next().ok_or("missing mesh count")?.parse()?;
    let mut out = String::from("{\"bones\":[");
    for (i, (name, parent, position)) in skeleton().iter().enumerate() {
        if i > 0 {
            out.push(',');
        }
        write!(
            out,
            "{{\"name\":{name:?},\"parent\":{parent},\"position\":{position:?}}}"
        )?;
    }
    out.push_str("],\"meshes\":[");
    let mut svg = String::from("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"1000\" viewBox=\"0 0 1200 1000\"><rect width=\"1200\" height=\"1000\" fill=\"#19212b\"/>");
    for m in 0..meshes {
        let name = lines.next().ok_or("missing mesh name")?;
        let uv_layers: usize = lines.next().ok_or("missing UV layers")?.parse()?;
        let textures: usize = lines.next().ok_or("missing textures")?.parse()?;
        let mut texture = "";
        for n in 0..textures {
            let path = lines.next().ok_or("missing texture path")?;
            if n == 0 {
                texture = path;
            }
            lines.next().ok_or("missing UV index")?;
        }
        let count: usize = lines.next().ok_or("missing vertices")?.parse()?;
        let (mut positions, mut normals, mut uvs) = (Vec::new(), Vec::new(), Vec::new());
        for _ in 0..count {
            positions.push(values::<f32>(lines.next().ok_or("missing position")?));
            normals.push(values::<f32>(lines.next().ok_or("missing normal")?));
            lines.next().ok_or("missing color")?;
            for uv in 0..uv_layers {
                let data = values::<f32>(lines.next().ok_or("missing UV")?);
                if uv == 0 {
                    uvs.push(data);
                }
            }
            lines.next().ok_or("missing bone indices")?;
            lines.next().ok_or("missing bone weights")?;
        }
        let faces: usize = lines.next().ok_or("missing faces")?.parse()?;
        let mut triangles = Vec::new();
        for _ in 0..faces {
            triangles.extend(values::<usize>(lines.next().ok_or("missing triangle")?));
        }
        if positions
            .iter()
            .any(|v| v.len() != 3 || v.iter().any(|x| !x.is_finite()))
            || triangles.iter().any(|i| *i >= count)
        {
            return Err("invalid mesh geometry".into());
        }
        let mut links: Vec<Vec<usize>> = vec![Vec::new(); count];
        let mut welded = std::collections::HashMap::new();
        for (i, p) in positions.iter().enumerate() {
            let key = (
                (p[0] * 10000.0).round() as i32,
                (p[1] * 10000.0).round() as i32,
                (p[2] * 10000.0).round() as i32,
            );
            if let Some(j) = welded.insert(key, i) {
                links[i].push(j);
                links[j].push(i);
            }
        }
        for f in triangles.chunks_exact(3) {
            for a in 0..3 {
                links[f[a]].push(f[(a + 1) % 3]);
                links[f[(a + 1) % 3]].push(f[a]);
            }
        }
        let mut labels = vec![usize::MAX; count];
        let mut groups = Vec::new();
        for i in 0..count {
            if labels[i] != usize::MAX {
                continue;
            }
            let id = groups.len();
            let mut group = Vec::new();
            let mut stack = vec![i];
            labels[i] = id;
            while let Some(j) = stack.pop() {
                group.push(j);
                for &k in &links[j] {
                    if labels[k] == usize::MAX {
                        labels[k] = id;
                        stack.push(k);
                    }
                }
            }
            groups.push(group);
        }
        for (id, g) in groups.iter().enumerate().filter(|(_, g)| g.len() > 25) {
            let b: Vec<_> = (0..3)
                .map(|a| {
                    (
                        g.iter()
                            .map(|i| positions[*i][a])
                            .fold(f32::INFINITY, f32::min),
                        g.iter()
                            .map(|i| positions[*i][a])
                            .fold(f32::NEG_INFINITY, f32::max),
                    )
                })
                .collect();
            println!("component {m}/{id}: {} vertices {b:?}", g.len());
        }
        // Remove the two disconnected bow halves from the saved combat equipment.
        let weapon: Vec<_> = groups
            .iter()
            .map(|g| g.iter().any(|i| positions[*i][2].abs() > 0.4))
            .collect();
        triangles.retain(|i| !weapon[labels[*i]]);
        // Keep long coat/cape panels on the torso instead of stretching them into wings.
        // OSRS uses flat faces, so split face corners where skin ownership changes.
        let (mut expanded_p, mut expanded_n, mut expanded_uv, mut arm_faces) =
            (Vec::new(), Vec::new(), Vec::new(), Vec::new());
        for face in triangles.chunks_exact(3) {
            let arm_face = face.iter().all(|i| {
                positions[*i][0].abs() > 0.19 && positions[*i][1] > 0.78 && positions[*i][1] < 1.46
            }) && face
                .iter()
                .all(|i| positions[*i][0].signum() == positions[face[0]][0].signum());
            for &i in face {
                expanded_p.push(positions[i].clone());
                expanded_n.push(normals[i].clone());
                expanded_uv.push(uvs[i].clone());
                arm_faces.push(arm_face);
            }
        }
        positions = expanded_p;
        normals = expanded_n;
        uvs = expanded_uv;
        let count = positions.len();
        triangles = (0..count).collect();
        let (mut bone_indices, mut weights) = (Vec::new(), Vec::new());
        for i in 0..count {
            positions[i][0] *= -1.;
            positions[i][2] *= -1.;
            normals[i][0] *= -1.;
            normals[i][2] *= -1.;
            let (indices, mut w, mut arm) = skin(&positions[i]);
            if arm > 0. {
                if arm_faces[i] {
                    w[0] = 0.;
                    w[1] /= arm;
                    w[2] /= arm;
                    arm = 1.;
                } else {
                    w[0] = 1.;
                    w[1] = 0.;
                    w[2] = 0.;
                    arm = 0.;
                }
            }
            let side = if positions[i][0] > 0. { 1. } else { -1. };
            let target = arm_pose(&positions[i], side);
            let n = vec![-side * normals[i][1], side * normals[i][0], normals[i][2]];
            for a in 0..3 {
                positions[i][a] = positions[i][a] * (1. - arm) + target[a] * arm;
                normals[i][a] = normals[i][a] * (1. - arm) + n[a] * arm;
            }
            bone_indices.push(indices);
            weights.push(w);
        }
        for axis in 0..2 {
            for face in triangles.chunks_exact(3) {
                let points: Vec<_> = face
                    .iter()
                    .map(|i| {
                        let p = &positions[*i];
                        format!(
                            "{},{}",
                            300.0 + axis as f32 * 600.0 + p[if axis == 0 { 0 } else { 2 }] * 440.0,
                            950.0 - p[1] * 440.0
                        )
                    })
                    .collect();
                write!(
                    svg,
                    "<polygon points=\"{}\" fill=\"{}\" stroke=\"#8fa5bb\" stroke-width=\"0.3\"/>",
                    points.join(" "),
                    if m == 0 { "#b9c6d2" } else { "#667788" }
                )?;
            }
        }
        if m > 0 {
            out.push(',');
        }
        write!(out,"{{\"name\":{name:?},\"texture\":{texture:?},\"positions\":{positions:?},\"normals\":{normals:?},\"uvs\":{uvs:?},\"triangles\":{triangles:?},\"boneIndices\":{bone_indices:?},\"weights\":{weights:?}}}")?;
        let bounds: Vec<_> = (0..3)
            .map(|a| {
                (
                    positions.iter().map(|p| p[a]).fold(f32::INFINITY, f32::min),
                    positions
                        .iter()
                        .map(|p| p[a])
                        .fold(f32::NEG_INFINITY, f32::max),
                )
            })
            .collect();
        println!("{name}: {count} vertices, {faces} faces, bounds {bounds:?}");
    }
    out.push_str("]}");
    svg.push_str("</svg>");
    let output = Path::new(&args[2]);
    fs::create_dir_all(output)?;
    fs::write(output.join("mesh.json"), out)?;
    fs::write(output.join("mesh-projections.svg"), svg)?;
    Ok(())
}
