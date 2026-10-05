"""Print the arguments a child process actually received (dev.ps1 regression only)."""
import json
import os
import sys

print(json.dumps({'argv': sys.argv[1:], 'temp': os.environ.get('TEMP'),
                  'tmp': os.environ.get('TMP'), 'tmpdir': os.environ.get('TMPDIR')}, ensure_ascii=True))
