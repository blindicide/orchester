from pathlib import Path
import re
import xml.etree.ElementTree as ET
root=Path(__file__).resolve().parents[1]
ns='{http://schemas.microsoft.com/winfx/2006/xaml}Key'
packs=[]
for culture in ('en-US','ru-RU'):
    tree=ET.parse(root/f'src/Orchester.Localization/Strings/{culture}.xaml')
    values={node.attrib[ns]:node.text for node in tree.getroot()}
    assert all(values.values()),f'Empty values in {culture}'
    assert len(values)==len(list(tree.getroot())),f'Duplicate resource in {culture}'
    packs.append(values)
assert packs[0].keys()==packs[1].keys(),'Localization key mismatch'
for file in root.glob('src/**/*.xaml'):
    ET.parse(file)
    for key in re.findall(r'\{DynamicResource (\w+)\}',file.read_text()):
        assert key in packs[0] or key in {'Surface','Ink','Panel'},(file,key)
for file in root.glob('src/**/*.csproj'): ET.parse(file)
for file in root.glob('src/**/*.cs'):
    for key in re.findall(r'(?:SafetyException|SetStatus)\("(\w+)"',file.read_text()):
        assert key in packs[0],(file,key)
print(f'PASS: {len(packs[0])} complete localization keys, XAML and project XML')
