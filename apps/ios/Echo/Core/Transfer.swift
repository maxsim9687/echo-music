import Foundation

/// One URLSession transfer with progress: download to a temp file, or upload a file and read the reply.
final class Transfer: NSObject, URLSessionDownloadDelegate, URLSessionDataDelegate, @unchecked Sendable {
    private let onProgress: (Double) -> Void
    private var downloadContinuation: CheckedContinuation<URL, Error>?
    private var uploadContinuation: CheckedContinuation<(Data, Int), Error>?
    private var movedURL: URL?
    private var moveError: Error?
    private var received = Data()

    private init(progress: @escaping (Double) -> Void) {
        onProgress = progress
        super.init()
    }

    private func report(_ p: Double) {
        let cb = onProgress
        DispatchQueue.main.async { cb(p) }
    }

    /// Downloads to a temporary file and returns its URL. `progress` is called on the main queue.
    static func download(_ url: URL, progress: @escaping (Double) -> Void) async throws -> URL {
        let t = Transfer(progress: progress)
        let session = URLSession(configuration: .default, delegate: t, delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }
        return try await withCheckedThrowingContinuation { (c: CheckedContinuation<URL, Error>) in
            t.downloadContinuation = c
            var req = URLRequest(url: url)
            req.setValue("1", forHTTPHeaderField: "X-Echo")
            session.downloadTask(with: req).resume()
        }
    }

    /// Uploads a file as the raw request body (POST) and returns (body, status).
    static func upload(_ url: URL, file: URL, contentType: String, progress: @escaping (Double) -> Void) async throws -> (Data, Int) {
        let t = Transfer(progress: progress)
        let session = URLSession(configuration: .default, delegate: t, delegateQueue: nil)
        defer { session.finishTasksAndInvalidate() }
        return try await withCheckedThrowingContinuation { (c: CheckedContinuation<(Data, Int), Error>) in
            t.uploadContinuation = c
            var req = URLRequest(url: url)
            req.httpMethod = "POST"
            req.timeoutInterval = 600
            req.setValue("1", forHTTPHeaderField: "X-Echo")
            req.setValue(contentType, forHTTPHeaderField: "Content-Type")
            session.uploadTask(with: req, fromFile: file).resume()
        }
    }

    // MARK: delegate

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didWriteData bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpectedToWrite: Int64) {
        guard totalBytesExpectedToWrite > 0 else { return }
        report(Double(totalBytesWritten) / Double(totalBytesExpectedToWrite))
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        let dest = FileManager.default.temporaryDirectory.appendingPathComponent("dl-" + UUID().uuidString)
        do {
            try FileManager.default.moveItem(at: location, to: dest)
            movedURL = dest
        } catch {
            moveError = error
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didSendBodyData bytesSent: Int64, totalBytesSent: Int64, totalBytesExpectedToSend: Int64) {
        guard totalBytesExpectedToSend > 0 else { return }
        report(Double(totalBytesSent) / Double(totalBytesExpectedToSend))
    }

    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        received.append(data)
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        let status = (task.response as? HTTPURLResponse)?.statusCode ?? 0
        if let c = downloadContinuation {
            downloadContinuation = nil
            if let error = error {
                c.resume(throwing: APIError(message: API.networkMessage(error), status: 0))
            } else if let e = moveError {
                c.resume(throwing: e)
            } else if let u = movedURL, status >= 200 && status < 300 {
                c.resume(returning: u)
            } else {
                if let u = movedURL { try? FileManager.default.removeItem(at: u) }
                c.resume(throwing: APIError(message: "HTTP \(status)", status: status))
            }
        }
        if let c = uploadContinuation {
            uploadContinuation = nil
            if let error = error {
                c.resume(throwing: APIError(message: API.networkMessage(error), status: 0))
            } else {
                c.resume(returning: (received, status))
            }
        }
    }
}
