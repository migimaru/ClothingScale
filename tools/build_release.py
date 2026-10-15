"""Build local or publication-ready VPM/UnityPackage artifacts (no upload)."""
import argparse
import gzip
import hashlib
import io
import json
import re
import tarfile
import time
import uuid
import zipfile
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parents[1]
GUID_NAMESPACE = uuid.UUID('5b2abb34-967c-4c50-89d6-5a79f49c5b61')
MAIN_SCRIPT_GUID = '72125998a3694a3408e486c90bfcc1e0'


def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def ensure_metadata(package):
    # Existing GUIDs are preserved on subsequent builds, including renamed files.
    entries = [package] + sorted(package.rglob('*'))
    for path in entries:
        if path.suffix == '.meta':
            continue
        meta = Path(str(path) + '.meta')
        if meta.exists():
            continue
        relative = path.relative_to(ROOT).as_posix()
        guid = (MAIN_SCRIPT_GUID if path.name == 'OutfitUpperArmScaleWindow.cs'
                else uuid.uuid5(GUID_NAMESPACE, relative).hex)
        header = 'fileFormatVersion: 2\nguid: ' + guid + '\n'
        if path.is_dir():
            content = header + 'folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
        elif path.suffix == '.cs':
            content = header + 'MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
        else:
            importer = ('AssemblyDefinitionImporter' if path.suffix == '.asmdef'
                        else 'PackageManifestImporter' if path.name == 'package.json'
                        else 'TextScriptImporter')
            content = header + importer + ':\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'
        meta.write_text(content, encoding='utf-8', newline='\n')


def public_manifest(manifest):
    config = json.loads((ROOT / 'release-config.json').read_text(encoding='utf-8'))
    required = ('repository_url', 'listing_url', 'author_name', 'author_email')
    missing = [key for key in required if not str(config.get(key, '')).strip()]
    if missing:
        raise ValueError('公開設定が未入力です: ' + ', '.join(missing))
    for key in ('repository_url', 'listing_url'):
        parsed = urlparse(config[key])
        if parsed.scheme != 'https' or not parsed.netloc:
            raise ValueError(key + ' はHTTPSの公開URLを指定してください')
    parsed = urlparse(config['repository_url'])
    if parsed.hostname != 'github.com' or len(parsed.path.strip('/').split('/')) != 2:
        raise ValueError('repository_url は https://github.com/所有者/リポジトリ 形式です')
    version, name = manifest['version'], manifest['name']
    result = dict(manifest)
    result['author'] = {'name': config['author_name'].strip(), 'email': config['author_email'].strip()}
    result['url'] = config['repository_url'].rstrip('/') + '/releases/download/' + version + '/' + name + '-' + version + '.zip'
    return result, config


def write_zip(path, files):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, data in sorted(files.items()):
            archive.writestr(name, data)


def tar_entry(archive, name, payload, timestamp):
    entry = tarfile.TarInfo(name)
    entry.mode = 0o777
    entry.mtime = timestamp
    entry.size = len(payload)
    archive.addfile(entry, io.BytesIO(payload))


def unity_package(path, entries):
    timestamp = int(time.time())
    seen = set()
    with path.open('wb') as output:
        with gzip.GzipFile(filename='archtemp.tar', mode='wb', fileobj=output, mtime=timestamp) as compressed:
            with tarfile.open(fileobj=compressed, mode='w', format=tarfile.GNU_FORMAT) as archive:
                for pathname, meta, payload in entries:
                    match = re.search(rb'^guid: ([0-9a-f]{32})$', meta, re.MULTILINE)
                    if not match:
                        raise ValueError('Invalid metadata GUID: ' + pathname)
                    guid = match.group(1).decode('ascii')
                    if guid in seen:
                        raise ValueError('Duplicate GUID: ' + guid)
                    seen.add(guid)
                    folder = tarfile.TarInfo(guid)
                    folder.type, folder.mode, folder.mtime = tarfile.DIRTYPE, 0o777, timestamp
                    archive.addfile(folder)
                    tar_entry(archive, guid + '/asset.meta', meta, timestamp)
                    tar_entry(archive, guid + '/pathname', pathname.encode('utf-8'), timestamp)
                    if payload is not None:
                        tar_entry(archive, guid + '/asset', payload, timestamp)


def verify(unity_path, entries, vpm_path, manifest):
    expected = {name: (meta, data) for name, meta, data in entries}
    actual = {}
    with tarfile.open(unity_path, 'r:gz') as archive:
        for entry in archive:
            if not entry.name.endswith('/pathname'):
                continue
            path = archive.extractfile(entry).read().decode('utf-8')
            assert path.startswith('Packages/' + manifest['name'] + '/') or path == 'Packages/' + manifest['name']
            guid = entry.name.split('/')[0]
            meta = archive.extractfile(guid + '/asset.meta').read()
            try:
                payload = archive.extractfile(guid + '/asset').read()
            except KeyError:
                payload = None
            actual[path] = (meta, payload)
    assert actual == expected, 'UnityPackage payload mismatch'
    with zipfile.ZipFile(vpm_path) as archive:
        assert json.loads(archive.read('package.json')) == manifest
        assert all(not name.startswith(('Assets/', 'Packages/')) for name in archive.namelist())
        assert archive.testzip() is None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--public', action='store_true', help='Require release-config.json and produce VPM index.json')
    args = parser.parse_args()
    candidates = list((ROOT / 'Packages').glob('*/package.json'))
    if len(candidates) != 1:
        raise ValueError('Expected exactly one package under Packages')
    package = candidates[0].parent
    manifest = json.loads(candidates[0].read_text(encoding='utf-8'))
    name, version = manifest['name'], manifest['version']
    if not re.fullmatch(r'[a-z0-9]+(?:[.-][a-z0-9]+)+', name) or package.name != name:
        raise ValueError('Package name/folder mismatch')
    if not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', version):
        raise ValueError('Invalid version')
    config = None
    if args.public:
        manifest, config = public_manifest(manifest)
    ensure_metadata(package)
    files = {path.relative_to(package).as_posix(): path.read_bytes()
             for path in package.rglob('*') if path.is_file()}
    files['package.json'] = json_bytes(manifest)
    dist = ROOT / 'dist'
    dist.mkdir(exist_ok=True)
    # Separate local/public outputs prevent replacing an already prepared public listing with a local build.
    if args.public:
        dist = dist / 'public'
        dist.mkdir(exist_ok=True)
    vpm = dist / (name + '-' + version + '.zip')
    write_zip(vpm, files)
    prefix = 'Packages/' + name
    root_meta = Path(str(package) + '.meta').read_bytes()
    install_files = {prefix + '/' + key: data for key, data in files.items()}
    install_files[prefix + '.meta'] = root_meta
    install_files['導入手順.md'] = (ROOT / '導入手順.md').read_bytes()
    write_zip(dist / 'UpperArmScaleTool-Packages-install.zip', install_files)
    entries = [(prefix, root_meta, None)]
    for path in sorted(package.rglob('*')):
        if path.suffix == '.meta':
            continue
        relative = path.relative_to(package).as_posix()
        entries.append((prefix + '/' + relative, Path(str(path) + '.meta').read_bytes(),
                        None if path.is_dir() else files[relative]))
    unity = dist / (name + '-' + version + '.unitypackage')
    unity_package(unity, entries)
    verify(unity, entries, vpm, manifest)
    digest = hashlib.sha256(vpm.read_bytes()).hexdigest()
    if config:
        listing_manifest = dict(manifest, zipSHA256=digest)
        listing = {'name': manifest['displayName'], 'id': name + '.repository',
                   'url': config['listing_url'], 'author': config['author_name'],
                   'packages': {name: {'versions': {version: listing_manifest}}}}
        (dist / 'index.json').write_bytes(json_bytes(listing))
    report = {'package': name, 'version': version, 'public': args.public,
              'unityPackageAssets': len(entries), 'vpmZipSHA256': digest,
              'checks': ['archive integrity', 'payload equality', 'Packages-only UnityPackage paths', 'unique GUIDs'],
              'unityImportVerified': False, 'vccInstallVerified': False}
    (dist / 'build-report.json').write_bytes(json_bytes(report))
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    try:
        main()
    except ValueError as error:
        raise SystemExit(str(error))
