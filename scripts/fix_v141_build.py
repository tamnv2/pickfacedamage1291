from pathlib import Path
p = Path(__file__).resolve().parents[1] / 'src/PickfaceDamage1291/DamageReportExportService.cs'
text = p.read_text(encoding='utf-8')
old = 'picture.MoveTo(ws.Cell(row, ImageColumn).Address, x, y);'
new = 'picture.MoveTo(ws.Cell(row, ImageColumn), x, y);'
if text.count(old) != 1:
    raise SystemExit(f'Expected one picture MoveTo marker, found {text.count(old)}')
text = text.replace(old, new, 1)
old2 = '''            var added = AddPictures(ws, row, resolved);\n            totalImages += added;'''
new2 = '''            var added = AddPictures(ws, row, resolved);\n            totalImages += added;\n            missingImages += Math.Max(0, resolved.Count - added);'''
if text.count(old2) != 1:
    raise SystemExit(f'Expected one image-count marker, found {text.count(old2)}')
text = text.replace(old2, new2, 1)
p.write_text(text, encoding='utf-8')
print('v1.4.1 build fix applied')
