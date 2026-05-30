using System;
using System.Security.Cryptography;
using System.IO;
using System.Text;

namespace UltraLiteDB
{
    /// <summary>
    /// Encryption AES wrapper to encrypt data pages
    /// </summary>
    internal class AesEncryption
    {
        private Aes _aes;

        /// <summary>
        /// Initializes AES encryption using a password and salt to derive the key and IV via PBKDF2.
        /// </summary>
        /// <param name="password">The password used to derive the encryption key.</param>
        /// <param name="salt">The salt bytes used in key derivation.</param>
        public AesEncryption(string password, byte[] salt)
        {
            _aes = Aes.Create();
            _aes.Padding = PaddingMode.Zeros;

            var pdb = new Rfc2898DeriveBytes(password, salt);

            using (pdb as IDisposable)
            {
                _aes.Key = pdb.GetBytes(32);
                _aes.IV = pdb.GetBytes(16);
            }
        }

        /// <summary>
        /// Encrypts a byte array, returning a new encrypted byte array with the same length as the original.
        /// </summary>
        /// <param name="bytes">The plaintext byte array to encrypt.</param>
        /// <returns>The encrypted byte array.</returns>
        public byte[] Encrypt(byte[] bytes)
        {
            using (var encryptor = _aes.CreateEncryptor())
            using (var stream = new MemoryStream())
            using (var crypto = new CryptoStream(stream, encryptor, CryptoStreamMode.Write))
            {
                crypto.Write(bytes, 0, bytes.Length);
                crypto.FlushFinalBlock();
                stream.Position = 0;
                var encrypted = new byte[stream.Length];
                stream.Read(encrypted, 0, encrypted.Length);

                return encrypted;
            }
        }

        /// <summary>
        /// Decrypts a byte array, returning a new plaintext byte array.
        /// </summary>
        /// <param name="encryptedValue">The encrypted byte array to decrypt.</param>
        /// <returns>The decrypted byte array.</returns>
        public byte[] Decrypt(byte[] encryptedValue)
        {
            using (var decryptor = _aes.CreateDecryptor())
            using (var stream = new MemoryStream())
            using (var crypto = new CryptoStream(stream, decryptor, CryptoStreamMode.Write))
            {
                crypto.Write(encryptedValue, 0, encryptedValue.Length);
                crypto.FlushFinalBlock();
                stream.Position = 0;
                var decryptedBytes = new Byte[stream.Length];
                stream.Read(decryptedBytes, 0, decryptedBytes.Length);

                return decryptedBytes;
            }
        }

        /// <summary>
        /// Hashes a password using SHA1 for password verification purposes.
        /// </summary>
        /// <param name="password">The password to hash.</param>
        /// <returns>The SHA1 hash of the password as a byte array.</returns>
        public static byte[] HashSHA1(string password)
        {
            var sha = SHA1.Create();
            var shaBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
            return shaBytes;
        }

        /// <summary>
        /// Generates a cryptographically random salt for use in key derivation, stored in the database header page.
        /// </summary>
        /// <param name="maxLength">The length of the salt in bytes (default: 16).</param>
        /// <returns>A byte array containing the random salt.</returns>
        public static byte[] Salt(int maxLength = 16)
        {
            var salt = new byte[maxLength];
            {
                var rng = RandomNumberGenerator.Create();
                using (rng as IDisposable)
                    rng.GetBytes(salt);
            }
            return salt;
        }

        /// <summary>
        /// Releases the AES encryption resources.
        /// </summary>
        public void Dispose()
        {
            _aes.Dispose();
        }
    }
}