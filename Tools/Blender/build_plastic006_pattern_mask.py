from pathlib import Path
import sys

import numpy as np
from PIL import Image


def main(source_path: str, output_path: str) -> None:
    source = Image.open(source_path).convert("L")
    luminance = np.asarray(source, dtype=np.float32) / 255.0
    low = float(np.percentile(luminance, 2.0))
    high = float(np.percentile(luminance, 99.0))
    normalized = np.clip((luminance - low) / max(high - low, 1e-5), 0.0, 1.0)
    normalized = np.power(normalized, 0.82)
    output = Image.fromarray(np.uint8(normalized * 255.0), mode="L")
    destination = Path(output_path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    output.save(destination, optimize=True)
    print(destination)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: build_plastic006_pattern_mask.py SOURCE_IMAGE OUTPUT_IMAGE")
    main(sys.argv[1], sys.argv[2])
