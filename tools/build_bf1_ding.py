"""Expose BF1's metallic confirmation layer for phone speakers, using a decoded source WAV."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile

import imageio_ffmpeg
import numpy as np
import soundfile as sf


def build(source, target):
    filters = ('atrim=start=0.30:end=1.25,asetpts=PTS-STARTPTS,'
               'highpass=f=1300,lowpass=f=8500,afade=t=in:d=0.004,afade=t=out:st=0.70:d=0.25')
    with tempfile.TemporaryDirectory(prefix='bf1-ding-') as temp:
        filtered = Path(temp) / 'filtered.wav'
        subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(), '-y', '-i', str(source), '-af', filters,
                        '-ac', '1', '-ar', '48000', '-c:a', 'pcm_f32le', str(filtered)], check=True,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        samples, rate = sf.read(filtered)
    samples /= max(np.max(np.abs(samples)), 1e-9)
    samples = np.tanh(samples * 3)  # Make the ringing audible without oversized transient peaks.
    samples *= .85 / np.max(np.abs(samples))
    sf.write(target, samples, rate, subtype='PCM_16')
    return dict(sourceWavSha256=hashlib.sha256(Path(source).read_bytes()).hexdigest(), filters=filters,
                dynamics='tanh(3 * peak-normalized samples); output peak 0.85',
                sampleRate=rate, channels=1, samples=len(samples), seconds=len(samples)/rate,
                rms=float(np.sqrt(np.mean(samples*samples))),
                outputSha256=hashlib.sha256(Path(target).read_bytes()).hexdigest())


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(build(args.source, args.out), indent=2))
