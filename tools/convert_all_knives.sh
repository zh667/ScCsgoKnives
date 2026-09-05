#!/usr/bin/env bash
# Batch-convert every CS knife record from the CS:MC client stream.
# Inputs come from tools/extract_knives.py; outputs land in .tmp-csmc/out.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p .tmp-csmc/out
for mesh in .tmp-csmc/assets/*.mesh.meshbin; do
  name=$(basename "$mesh" .mesh.meshbin)
  dotnet run --project tools/CsmcAssetConverter/CsmcAssetConverter.csproj -c Release --no-build -- \
    --mesh "$mesh" --anim ".tmp-csmc/assets/$name.anim.animbin" \
    --out ".tmp-csmc/out/$name.diag.json" \
    --obj-parts-dir ".tmp-csmc/out/parts/$name" \
    --runtime ".tmp-csmc/out/$name.animation.json" > ".tmp-csmc/out/$name.log" 2>&1 \
    || { echo "FAILED $name"; tail -3 ".tmp-csmc/out/$name.log"; continue; }
  echo "ok $name"
done
