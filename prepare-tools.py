from pathlib import Path
import zipfile,hashlib,json
root=Path(__file__).resolve().parent
manifest=json.loads((root/'TOOLS.json').read_text())
for item in manifest:
    p=root/'tools'/item['file']
    if hashlib.sha256(p.read_bytes()).hexdigest()!=item['sha256']:raise ValueError('Componente divergente: '+p.name)
with zipfile.ZipFile(root/'assets/tools.zip','w',zipfile.ZIP_DEFLATED) as archive:
    for name in ['ffmpeg.exe','ffprobe.exe','FFMPEG-LICENSE.txt']:archive.write(root/'tools'/name,name)
print('Bundle de gravação pronto.')
