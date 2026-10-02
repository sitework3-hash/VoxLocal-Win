import Foundation
import Security

public enum SecretStoreError: Error, Equatable {
    case invalidText
    case unexpectedStatus(OSStatus)
}

public protocol SecretStoring: Sendable {
    func read() throws -> String?
    func save(_ secret: String) throws
    func delete() throws
}

/// Stores the cloud API key in the user's login Keychain. The key is never
/// persisted in UserDefaults or written to the application log.
public final class KeychainSecretStore: SecretStoring, @unchecked Sendable {
    public static let shared = KeychainSecretStore()

    private let service: String
    private let account: String

    public init(service: String = "org.voxlocal.VoxLocal.cloud-refinement", account: String = "api-key") {
        self.service = service
        self.account = account
    }

    public func read() throws -> String? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw SecretStoreError.unexpectedStatus(status) }
        guard let data = result as? Data, let value = String(data: data, encoding: .utf8) else {
            throw SecretStoreError.invalidText
        }
        return value
    }

    public func save(_ secret: String) throws {
        let normalized = secret.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalized.isEmpty, let data = normalized.data(using: .utf8) else {
            throw SecretStoreError.invalidText
        }

        let status = SecItemUpdate(
            baseQuery as CFDictionary,
            [kSecValueData as String: data] as CFDictionary)
        if status == errSecSuccess { return }
        if status != errSecItemNotFound { throw SecretStoreError.unexpectedStatus(status) }

        var item = baseQuery
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        let addStatus = SecItemAdd(item as CFDictionary, nil)
        guard addStatus == errSecSuccess else { throw SecretStoreError.unexpectedStatus(addStatus) }
    }

    public func delete() throws {
        let status = SecItemDelete(baseQuery as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw SecretStoreError.unexpectedStatus(status)
        }
    }

    private var baseQuery: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
    }
}
