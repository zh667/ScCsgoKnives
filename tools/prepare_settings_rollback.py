"""Restore UI/audio semantics without rolling back later gameplay or resources."""
import json,shutil,subprocess
import imageio_ffmpeg
from prepare_codec_release import write,sha,dump
import prepare_gameplay_followup as prep
ROOT=prep.ROOT;S=ROOT/".tmp/settings-rollback-130-20260927";OLD=ROOT/".tmp/gameplay-audio-130-20260927"
BASELINES={"core":("轻量","6f07d77baaa32706f327df90eefc7f09768075a7e3454f396c1a9e1bb6b53c90"),
           "agents":("探员","08cec2ad2e33ab05455f7f43911c6bd6dd00dfd8d2882e2b7413429722587552")}
FULL_SHA="83b81d75d3c69133e6e6cb805f772e808a93516571fa44e32d12fea1abaa652d"
def main():
    prep.S=S;prep.OLD=OLD;prep.BASELINES=BASELINES;prep.FULL_SHA=FULL_SHA;prep.main()
    source=ROOT.parent/"CSMCReverse/local_cs2_analysis/all_weapons/05_audio/decoded/sounds/weapons/molotov/molotov_detonate_air_01.wav"
    original=source.read_bytes()
    write(ROOT/"src/ScCsgoKnives/Assets/Audio/ScCsgoKnives/grenade_fire_airburst.wav",original)
    write(S/"new-assets/grenade_fire_airburst.wav",original)
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),"-hide_banner","-loglevel","error","-y","-i",str(source),
        "-ac","1","-ar","32000","-c:a","libvorbis","-q:a","3",str(S/"new-assets/grenade_fire_airburst.ogg")],check=True)
    dump(S/"airburst-source.json",dict(source=str(source),sourceSha256=sha(original),
        conversion="Full original CS2 WAV; Lite mono 32kHz Vorbis q3, original duration and playback pitch.",
        assets={p.name:dict(bytes=p.stat().st_size,sha256=sha(p.read_bytes())) for p in (S/"new-assets").glob("grenade_fire_airburst.*")}))
    for prefix in ["","full/"]:
        obsolete=(S/(prefix+"core/source/World/ScOwnedAudio.cs")).resolve()
        assert obsolete.is_relative_to(S.resolve())
        if obsolete.exists():obsolete.unlink()
    print("Staged exact previous audio callers and new layout-only HUD editor.")
if __name__=="__main__":main()
