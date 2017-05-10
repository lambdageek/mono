//
// X509CAPI.cs - NUnit Test Cases for X509 CAPI integration (IntPtr constructor)
//
// Author:
//	Sebastien Pouliot  <sebastien@ximian.com>
//
// (C) 2003 Motus Technologies Inc. (http://www.motus.com)
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
// Copyright 2011 Xamarin Inc (http://www.xamarin.com).
//
using NUnit.Framework;

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;

namespace MonoTests.System.Security.Cryptography.X509Certificates {

	// This test is ONLY a simulation as we can't test CryptoAPI integration
	// outside Windows. The simulation create a buffer similar to what CAPI
	// would return as a valid IntPtr to a CERT_CONTEXT structure.

	// There is a commented "main" at the end of the file that can be used
	// to verify, under Windows, that the CERT_CONTEXT used for this test
	// is similar to the one provided by CAPI.

	[TestFixture]
	public class X509CAPI {

		// copied from X509Certificate for test uses only
		[StructLayout (LayoutKind.Sequential)]
		internal struct CertificateContext {
			public UInt32 dwCertEncodingType;
			public IntPtr pbCertEncoded;
			public UInt32 cbCertEncoded;
			public IntPtr pCertInfo;
			public IntPtr hCertStore;
		}

		public void AssertEquals (string msg, byte[] array1, byte[] array2) 
		{
			Assert.AreEqual (array1, array2, msg);
		}

		public string ToString (byte[] data) 
		{
			StringBuilder sb = new StringBuilder ();
			for (int i = 0; i < data.Length; i++)
				sb.Append (data[i].ToString ("X2"));
			return sb.ToString ();
		}

		private CultureInfo oldcult;

		[SetUp]
		public void SetUp () 
		{
			// the current culture determines the result of formatting
			oldcult = Thread.CurrentThread.CurrentCulture;
			Thread.CurrentThread.CurrentCulture = new CultureInfo ("");
		}

		[TearDown]
		public void TearDown () 
		{
			Thread.CurrentThread.CurrentCulture = oldcult;
			File.Delete("temp.cer");
			File.Delete("temp.b64");
		}

#if !MOBILE && !MONOMAC
		public IntPtr GetHandleEx (byte[] certificate) 
		{
			CertificateContext cc = new CertificateContext ();
//			cc.dwCertEncodingType = 0x10001; // PKCS_7_ASN_ENCODING | X509_ASN_ENCODING
//			cc.dwCertEncodingType = 0x10000; // PKCS_7_ASN_ENCODING 
			cc.dwCertEncodingType = 0x1; // X509_ASN_ENCODING
			cc.cbCertEncoded = (UInt32) certificate.Length;
			cc.pbCertEncoded = Marshal.AllocCoTaskMem (certificate.Length);
			cc.pCertInfo = (IntPtr) 0;
			cc.hCertStore = (IntPtr) 0;
			Marshal.Copy (certificate, 0, cc.pbCertEncoded, certificate.Length);

			int size = Marshal.SizeOf (cc.GetType ());
			IntPtr result = Marshal.AllocCoTaskMem (size);
			Marshal.StructureToPtr (cc, result, false);
			return result;
		}

		[DllImport(@"crypt32.dll")]
		internal static extern IntPtr CertCreateCertificateContext(int dwCertEncodingType, IntPtr pbCertEncoded, int cbCertEncoded);

		public IntPtr GetHandle (byte[] certificate) 
		{
			IntPtr data = Marshal.AllocHGlobal (certificate.Length);
			Marshal.Copy (certificate, 0, data, certificate.Length);
			IntPtr handle = CertCreateCertificateContext (0x10001, data, certificate.Length);
			Marshal.FreeHGlobal (data);
			return handle;
		}

		[Test]
#if MOBILE || MONOMAC
		[Ignore ("This constructor always throw a NotSupportedException under MOBILE and is useless without CryptoAPI (i.e. outside Windows)")]
#endif
		public void ConstructorIntPtr ()
		{
			// This test uses a certificate format that only works
			if (PlatformID.Win32NT != Environment.OSVersion.Platform)
				return;

			byte[] cert = { 0x30,0x82,0x01,0xFF,0x30,0x82,0x01,0x6C,0x02,0x05,0x02,0x72,0x00,0x06,0xE8,0x30,0x0D,0x06,0x09,0x2A,0x86,0x48,0x86,0xF7,0x0D,0x01,0x01,0x02,0x05,0x00,0x30,0x5F,0x31,0x0B,0x30,0x09,0x06,0x03,0x55,0x04,0x06,0x13,0x02,0x55,0x53,0x31,0x20,0x30,0x1E,0x06,0x03,0x55,0x04,0x0A,0x13,0x17,0x52,0x53,0x41,0x20,0x44,0x61,0x74,0x61,0x20,0x53,0x65,0x63,0x75,0x72,0x69,0x74,0x79,0x2C,0x20,0x49,0x6E,0x63,0x2E,0x31,0x2E,0x30,0x2C,0x06,0x03,0x55,0x04,0x0B,0x13,0x25,0x53,0x65,0x63,0x75,0x72,0x65,0x20,0x53,0x65,0x72,0x76,
				0x65,0x72,0x20,0x43,0x65,0x72,0x74,0x69,0x66,0x69,0x63,0x61,0x74,0x69,0x6F,0x6E,0x20,0x41,0x75,0x74,0x68,0x6F,0x72,0x69,0x74,0x79,0x30,0x1E,0x17,0x0D,0x39,0x36,0x30,0x33,0x31,0x32,0x31,0x38,0x33,0x38,0x34,0x37,0x5A,0x17,0x0D,0x39,0x37,0x30,0x33,0x31,0x32,0x31,0x38,0x33,0x38,0x34,0x36,0x5A,0x30,0x61,0x31,0x0B,0x30,0x09,0x06,0x03,0x55,0x04,0x06,0x13,0x02,0x55,0x53,0x31,0x13,0x30,0x11,0x06,0x03,0x55,0x04,0x08,0x13,0x0A,0x43,0x61,0x6C,0x69,0x66,0x6F,0x72,0x6E,0x69,0x61,0x31,0x14,0x30,0x12,0x06,0x03,
				0x55,0x04,0x0A,0x13,0x0B,0x43,0x6F,0x6D,0x6D,0x65,0x72,0x63,0x65,0x4E,0x65,0x74,0x31,0x27,0x30,0x25,0x06,0x03,0x55,0x04,0x0B,0x13,0x1E,0x53,0x65,0x72,0x76,0x65,0x72,0x20,0x43,0x65,0x72,0x74,0x69,0x66,0x69,0x63,0x61,0x74,0x69,0x6F,0x6E,0x20,0x41,0x75,0x74,0x68,0x6F,0x72,0x69,0x74,0x79,0x30,0x70,0x30,0x0D,0x06,0x09,0x2A,0x86,0x48,0x86,0xF7,0x0D,0x01,0x01,0x01,0x05,0x00,0x03,0x5F,0x00,0x30,0x5C,0x02,0x55,0x2D,0x58,0xE9,0xBF,0xF0,0x31,0xCD,0x79,0x06,0x50,0x5A,0xD5,0x9E,0x0E,0x2C,0xE6,0xC2,0xF7,0xF9,
				0xD2,0xCE,0x55,0x64,0x85,0xB1,0x90,0x9A,0x92,0xB3,0x36,0xC1,0xBC,0xEA,0xC8,0x23,0xB7,0xAB,0x3A,0xA7,0x64,0x63,0x77,0x5F,0x84,0x22,0x8E,0xE5,0xB6,0x45,0xDD,0x46,0xAE,0x0A,0xDD,0x00,0xC2,0x1F,0xBA,0xD9,0xAD,0xC0,0x75,0x62,0xF8,0x95,0x82,0xA2,0x80,0xB1,0x82,0x69,0xFA,0xE1,0xAF,0x7F,0xBC,0x7D,0xE2,0x7C,0x76,0xD5,0xBC,0x2A,0x80,0xFB,0x02,0x03,0x01,0x00,0x01,0x30,0x0D,0x06,0x09,0x2A,0x86,0x48,0x86,0xF7,0x0D,0x01,0x01,0x02,0x05,0x00,0x03,0x7E,0x00,0x54,0x20,0x67,0x12,0xBB,0x66,0x14,0xC3,0x26,0x6B,0x7F,
				0xDA,0x4A,0x25,0x4D,0x8B,0xE0,0xFD,0x1E,0x53,0x6D,0xAC,0xA2,0xD0,0x89,0xB8,0x2E,0x90,0xA0,0x27,0x43,0xA4,0xEE,0x4A,0x26,0x86,0x40,0xFF,0xB8,0x72,0x8D,0x1E,0xE7,0xB7,0x77,0xDC,0x7D,0xD8,0x3F,0x3A,0x6E,0x55,0x10,0xA6,0x1D,0xB5,0x58,0xF2,0xF9,0x0F,0x2E,0xB4,0x10,0x55,0x48,0xDC,0x13,0x5F,0x0D,0x08,0x26,0x88,0xC9,0xAF,0x66,0xF2,0x2C,0x9C,0x6F,0x3D,0xC3,0x2B,0x69,0x28,0x89,0x40,0x6F,0x8F,0x35,0x3B,0x9E,0xF6,0x8E,0xF1,0x11,0x17,0xFB,0x0C,0x98,0x95,0xA1,0xC2,0xBA,0x89,0x48,0xEB,0xB4,0x06,0x6A,0x22,0x54,
				0xD7,0xBA,0x18,0x3A,0x48,0xA6,0xCB,0xC2,0xFD,0x20,0x57,0xBC,0x63,0x1C };

			// will only work on Windows (MS or Mono)
			// this isn't much of a problem because the structure CERT_CONTEXT (IntPtr)
			// is specific to CryptoAPI/Windows.
			IntPtr handle = IntPtr.Zero;

			if (Path.DirectorySeparatorChar == '\\') {
				handle = GetHandle (cert);
			} else {
				handle = GetHandleEx (cert);
			}

			X509Certificate x509 = new X509Certificate (handle);
			byte[] hash = { 0xD6,0x2F,0x48,0xD0,0x13,0xEE,0x7F,0xB5,0x8B,0x79,0x07,0x45,0x12,0x67,0x0D,0x9C,0x5B,0x3A,0x5D,0xA9 };
			Assert.AreEqual (hash, x509.GetCertHash (), "GetCertHash");
			Assert.AreEqual ("D62F48D013EE7FB58B79074512670D9C5B3A5DA9", x509.GetCertHashString (), "GetCertHashString");
			DateTime from = DateTime.ParseExact (x509.GetEffectiveDateString (), "MM/dd/yyyy HH:mm:ss", null).ToUniversalTime ();
			Assert.AreEqual ("03/12/1996 18:38:47", from.ToString (), "GetEffectiveDateString");
			DateTime until = DateTime.ParseExact (x509.GetExpirationDateString (), "MM/dd/yyyy HH:mm:ss", null).ToUniversalTime ();
			Assert.AreEqual ("03/12/1997 18:38:46", until.ToString (), "GetExpirationDateString");
			Assert.AreEqual ("X509", x509.GetFormat (), "GetFormat");
			Assert.AreEqual (-701544240, x509.GetHashCode (), "GetHashCode");
			Assert.AreEqual ("C=US, O=\"RSA Data Security, Inc.\", OU=Secure Server Certification Authority", x509.GetIssuerName (), "GetIssuerName");
			Assert.AreEqual ("1.2.840.113549.1.1.1", x509.GetKeyAlgorithm (), "GetKeyAlgorithm");
			byte[] keyparams = { 0x05,0x00 };
			Assert.AreEqual (keyparams, x509.GetKeyAlgorithmParameters (), "GetKeyAlgorithmParameters");
			Assert.AreEqual ("0500", x509.GetKeyAlgorithmParametersString (), "GetKeyAlgorithmParametersString");
			Assert.AreEqual ("C=US, S=California, O=CommerceNet, OU=Server Certification Authority", x509.GetName (), "GetName");
			byte[] pubkey = { 0x30,0x5C,0x02,0x55,0x2D,0x58,0xE9,0xBF,0xF0,0x31,0xCD,0x79,0x06,0x50,0x5A,0xD5,0x9E,0x0E,0x2C,0xE6,0xC2,0xF7,0xF9,0xD2,0xCE,0x55,0x64,0x85,0xB1,0x90,0x9A,0x92,0xB3,0x36,0xC1,0xBC,0xEA,0xC8,0x23,0xB7,0xAB,0x3A,0xA7,0x64,0x63,0x77,0x5F,0x84,0x22,0x8E,0xE5,0xB6,0x45,0xDD,0x46,0xAE,0x0A,0xDD,0x00,0xC2,0x1F,0xBA,0xD9,0xAD,0xC0,0x75,0x62,0xF8,0x95,0x82,0xA2,0x80,0xB1,0x82,0x69,0xFA,0xE1,0xAF,0x7F,0xBC,0x7D,0xE2,0x7C,0x76,0xD5,0xBC,0x2A,0x80,0xFB,0x02,0x03,0x01,0x00,0x01 };
			Assert.AreEqual (pubkey, x509.GetPublicKey (), "GetPublicKey");
			Assert.AreEqual ("305C02552D58E9BFF031CD7906505AD59E0E2CE6C2F7F9D2CE556485B1909A92B336C1BCEAC823B7AB3AA76463775F84228EE5B645DD46AE0ADD00C21FBAD9ADC07562F89582A280B18269FAE1AF7FBC7DE27C76D5BC2A80FB0203010001", x509.GetPublicKeyString (), "GetPublicKeyString");
			Assert.AreEqual (cert, x509.GetRawCertData (), "GetRawCertData");
			Assert.AreEqual (ToString (cert), x509.GetRawCertDataString (), "GetRawCertDataString");
			byte[] serial = { 0xE8,0x06,0x00,0x72,0x02 };
			Assert.AreEqual (serial, x509.GetSerialNumber (), "GetSerialNumber");
			Assert.AreEqual ("02720006E8", x509.GetSerialNumberString (), "GetSerialNumberString");
		}
#endif

		[Test]
		[ExpectedException (typeof (ArgumentException))]
		public void ConstructorIntPtrZero ()
		{
			X509Certificate spc = new X509Certificate (IntPtr.Zero);
			Assert.AreEqual (0, spc.GetHashCode (), "ConstructorIntPtrZero");
		}
	}
}
