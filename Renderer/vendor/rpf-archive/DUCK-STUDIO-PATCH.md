# Duck Studio dependency patch

This is the published rpf-archive 0.10.0 crate from https://crates.io/crates/rpf-archive/0.10.0, retaining its source, required resources and Unlicense.

Duck Studio changes the ruzstd requirement from 0.8 to 0.9.1 to address [RUSTSEC-2026-0344](https://rustsec.org/advisories/RUSTSEC-2026-0344.html), a decompression denial of service. The renderer selects this copy through `[patch.crates-io]`. Comments and documentation use ordinary hyphens.

Remove this patch when upstream provides a compatible release with the fixed dependency. This directory is library source, not user game files, projects or credentials.
