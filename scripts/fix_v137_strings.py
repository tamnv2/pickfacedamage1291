from pathlib import Path

p = Path('src/PickfaceDamage1291/MainFormV3.cs')
text = p.read_text(encoding='utf-8')

pairs = [
    (
'''                "Toàn bộ thông tin và hình ảnh của phiếu này trùng hoàn toàn với một phiếu đã có. Hệ thống không tạo thêm bản ghi trùng.

ID phiếu đã có: " + duplicateId,''',
 r'''                "Toàn bộ thông tin và hình ảnh của phiếu này trùng hoàn toàn với một phiếu đã có. Hệ thống không tạo thêm bản ghi trùng.\n\nID phiếu đã có: " + duplicateId,'''
    ),
    (
'''            $"Xoá {reports.Count:N0} phiếu đã chọn khỏi danh sách?

Google sẽ được ghi dấu đã xoá để dữ liệu không tự xuất hiện lại khi đồng bộ. Hành động này được ghi vào lịch sử.",''',
 r'''            $"Xoá {reports.Count:N0} phiếu đã chọn khỏi danh sách?\n\nGoogle sẽ được ghi dấu đã xoá để dữ liệu không tự xuất hiện lại khi đồng bộ. Hành động này được ghi vào lịch sử.",'''
    ),
    (
'''        var detail = string.Join("
", failures.Take(8));
        if (failures.Count > 8) detail += $"
... và {failures.Count - 8:N0} phiếu khác.";''',
 r'''        var detail = string.Join("\n", failures.Take(8));
        if (failures.Count > 8) detail += $"\n... và {failures.Count - 8:N0} phiếu khác.";'''
    ),
    (
'''            $"Đã xoá {deleted.Count:N0}/{reports.Count:N0} phiếu. Các phiếu lỗi vẫn được giữ nguyên để tránh mất dữ liệu không đồng bộ.

{detail}",''',
 r'''            $"Đã xoá {deleted.Count:N0}/{reports.Count:N0} phiếu. Các phiếu lỗi vẫn được giữ nguyên để tránh mất dữ liệu không đồng bộ.\n\n{detail}",'''
    ),
]

for old, new in pairs:
    if old not in text:
        raise SystemExit('expected invalid multiline string not found: ' + old[:80])
    text = text.replace(old, new, 1)

p.write_text(text, encoding='utf-8', newline='\n')
print('C# message strings escaped correctly')
