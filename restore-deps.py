from pathlib import Path
import json, urllib.request, zipfile, io
root=Path(__file__).resolve().parent
for item in json.loads((root/'DEPENDENCIAS.json').read_text()):
    name=item['id'].lower();version=item['version'].lower()
    data=urllib.request.urlopen(f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg').read()
    dest=(root/'deps'/item['folder']).resolve();dest.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        for entry in archive.infolist():
            target=(dest/entry.filename).resolve()
            if not target.is_relative_to(dest):raise ValueError('Caminho inválido no pacote NuGet')
        archive.extractall(dest)
print('Dependências restauradas nas versões fixadas.')
