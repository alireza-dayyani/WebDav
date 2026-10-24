"""Generate Inkstone's original geometric icon. Requires Pillow, only for asset maintenance."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><rect width="64" height="64" rx="16" fill="#155e50"/><path d="M20 15h24v5H20zm10 5h5v24h-5zM20 44h24v5H20z" fill="#f5efdf"/><circle cx="45" cy="33" r="3" fill="#c8d6a5"/></svg>\n'
icons = root / 'assets/app-icon'
def icon(size):
    scale = size * 4 / 64
    image = Image.new('RGBA', (size * 4, size * 4))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((0, 0, size * 4 - 1, size * 4 - 1), radius=16 * scale, fill='#155e50')
    for box in [(20, 15, 44, 20), (30, 20, 35, 44), (20, 44, 44, 49)]:
        draw.rectangle(tuple(v * scale for v in box), fill='#f5efdf')
    draw.ellipse(tuple(v * scale for v in (42, 30, 48, 36)), fill='#c8d6a5')
    return image.resize((size, size), Image.Resampling.LANCZOS)
for path in icons.rglob('*'):
    if path.suffix == '.png':
        size = int(path.parent.parent.name.split('x')[0]) if 'hicolor' in str(path) else 512 if '512' in path.name else 1024
        icon(size).save(path)
    elif path.suffix == '.svg':
        path.write_text(svg, encoding='utf-8')
icon(256).save(icons / 'inkstone.ico', sizes=[(16, 16), (32, 32), (48, 48), (128, 128), (256, 256)])
icon(1024).save(icons / 'inkstone.icns')
background = root / 'packaging/macos/dmg/background.svg'
background.write_text('<svg xmlns="http://www.w3.org/2000/svg" width="660" height="400"><rect width="660" height="400" fill="#f1f3ed"/><text x="330" y="65" text-anchor="middle" font-family="serif" font-size="32" fill="#155e50">Inkstone</text><text x="330" y="330" text-anchor="middle" font-family="sans-serif" font-size="16" fill="#55765f">Drag the app to Applications</text></svg>\n', encoding='utf-8')
Image.new('RGB', (660, 400), '#f1f3ed').save(background.with_suffix('.tiff'))
print('Generated original Inkstone icons and packaging background.')
