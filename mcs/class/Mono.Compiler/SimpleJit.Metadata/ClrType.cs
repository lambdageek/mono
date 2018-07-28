using System;

namespace SimpleJit.Metadata
{
        public enum NumericCatgoery
        {
                NAN,
                Int,
                Float,
                NativeInt
        }

	public struct ClrType 
        {
		private System.RuntimeTypeHandle rttype;
                private NumericCatgoery numerical;
                private int precision;
                private bool signed;

		internal ClrType (
                        System.RuntimeTypeHandle rttype,
                        NumericCatgoery numerical,
                        int precision,
                        bool signed) {
			this.rttype = rttype;
                        this.numerical = numerical;
                        this.precision = precision;
                        this.signed = signed;
		}

                internal ClrType (System.RuntimeTypeHandle rttype) 
                        : this (rttype, NumericCatgoery.NAN, 0, false) {
		}

                /// The numeric category of this type
                public NumericCatgoery NumCat => numerical;

                /// Undefined if the type is not numerical
                public int Precision => precision;
                /// Undefined if the type is not numerical
                public bool Signed => signed;

		/// Escape hatch, use sparingly
		public Type AsSystemType { get => Type.GetTypeFromHandle (rttype); }

		public override bool Equals (object obj)
		{
			if (obj == null || GetType () != obj.GetType ())
				return false;
			return rttype.Equals (((ClrType)obj).rttype);
		}

		public bool Equals (ClrType other)
		{
			return rttype.Equals (other.rttype);
		}

		public override int GetHashCode ()
		{
			return rttype.GetHashCode ();
		}

		public static bool operator == (ClrType left, ClrType right)
		{
			return left.Equals (right);
		}

		public static bool operator != (ClrType left, ClrType right)
		{
			return !left.Equals (right);
		}

		public static ClrType MakePointerType (ClrType ty)
		{
			return new ClrType (ty.AsSystemType.MakePointerType ().TypeHandle);
		}
	}
}
