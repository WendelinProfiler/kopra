import Foundation

enum FileSearch {
    // mdfind returns filesystem paths, never localized Finder display names.
    // NUL framing preserves filenames that contain a line break.
    static func urls(_ data: Data, limit: Int = 20) -> [URL] {
        var seen = Set<String>()
        return data.split(separator: 0).compactMap { bytes -> URL? in
            guard let path = String(data: Data(bytes), encoding: .utf8),
                  path.hasPrefix("/"), FileManager.default.fileExists(atPath: path),
                  seen.insert(path).inserted else { return nil }
            return URL(fileURLWithPath: path)
        }.prefix(max(0, limit)).map { $0 }
    }
}
