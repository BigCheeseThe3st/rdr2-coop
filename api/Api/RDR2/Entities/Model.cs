//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/scripthookvdotnet#license
//

using RDR2.Math;
using RDR2.Native;
using System;

namespace RDR2
{
	public struct Model : IEquatable<Model>, INativeValue
	{
		public Model(int hash)
		{
			Hash = hash;
		}
		public Model(string name) : this(Game.Joaat(name))
		{
		}
		public Model(uint hash) : this((int)hash)
		{
		}
		public Model(PedHash hash) : this((int)hash)
		{
		}

		public ulong NativeValue
		{
			get => (ulong)Hash;
			set => Hash = unchecked((int)value);
		}

		/// <summary>
		/// Gets the hash for this <see cref="Model"/>.
		/// </summary>
		public int Hash
		{
			get; private set;
		}

		/// <summary>
		/// Gets if this <see cref="Model"/> is valid.
		/// </summary>
		public bool IsValid => STREAMING.IS_MODEL_VALID((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is in the CD image.
		/// </summary>
		public bool IsInCdImage => STREAMING.IS_MODEL_IN_CDIMAGE((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is loaded so it can be spawned.
		/// </summary>
		public bool IsLoaded => STREAMING.HAS_MODEL_LOADED((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether the collision for this <see cref="Model"/> is loaded.
		/// </summary>
		public bool IsCollisionLoaded => STREAMING.HAS_COLLISION_FOR_MODEL_LOADED((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is a ped.
		/// </summary>
		public bool IsPed => STREAMING.IS_MODEL_A_PED((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is an object.
		/// </summary>
		public bool IsObject => STREAMING._IS_MODEL_AN_OBJECT((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is a boat.
		/// </summary>
		public bool IsBoat => VEHICLE.IS_THIS_MODEL_A_BOAT((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is a train.
		/// </summary>
		public bool IsTrain => VEHICLE.IS_THIS_MODEL_A_TRAIN((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is vehicle.
		/// </summary>
		public bool IsVehicle => STREAMING.IS_MODEL_A_VEHICLE((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is a draft vehicle.
		/// </summary>
		public bool IsDraftVehicle => VEHICLE._IS_THIS_MODEL_A_DRAFT_VEHICLE((uint)Hash);

		/// <summary>
		/// Gets a value indicating whether this <see cref="Model"/> is horse.
		/// </summary>
		public bool IsHorse => PED._IS_THIS_MODEL_A_HORSE((uint)Hash);

		/// <summary>
		/// Request this <see cref="Model"/> to be loaded into memory. Remember to mark this <see cref="Model"/> as no longer needed when you are done with this <see cref="Model"/>.
		/// </summary>
		public void Request()
		{
			if (!STREAMING.IS_MODEL_VALID((uint)Hash)) { return; }
			STREAMING.REQUEST_MODEL((uint)Hash, false);
		}

		/// <summary>
		/// Request this <see cref="Model"/> to be loaded into memory. Remember to mark this <see cref="Model"/> as no longer needed when you are done with this <see cref="Model"/>.
		/// </summary>
		public bool Request(int timeout)
		{
			Request();

			DateTime endtime = timeout >= 0 ? DateTime.UtcNow + new TimeSpan(0, 0, 0, 0, timeout) : DateTime.MaxValue;

			while (!IsLoaded)
			{
				Script.Yield();

				if (DateTime.UtcNow >= endtime)
					return false;
			}

			return true;
		}

		/// <summary>
		/// Gets the dimensions of this <see cref="Model"/>.
		/// </summary>
		/// <returns>
		/// rearBottomLeft is the minimum dimensions, which contains the rear bottom left relative offset from the origin of the model,
		///  frontTopRight is the maximum dimensions, which contains the front top right relative offset from the origin of the model.
		/// </returns>
		public (Vector3 rearBottomLeft, Vector3 frontTopRight) Dimensions
		{
			get
			{
				Vector3 min, max;
				unsafe { MISC.GET_MODEL_DIMENSIONS((uint)Hash, &min, &max); }
				return (min, max);
			}
		}

		public bool Equals(Model obj)
		{
			return Hash == obj.Hash;
		}
		public override bool Equals(object obj)
		{
			return obj != null && obj.GetType() == GetType() && Equals((Model)obj);
		}

		public static bool operator ==(Model left, Model right)
		{
			return left.Equals(right);
		}
		public static bool operator !=(Model left, Model right)
		{
			return !left.Equals(right);
		}

		public static implicit operator int(Model source)
		{
			return source.Hash;
		}
		public static implicit operator PedHash(Model source)
		{
			return (PedHash)source.Hash;
		}
		public static implicit operator uint(Model source)
		{
			return (uint)source.Hash;
		}

		public static implicit operator Model(int source)
		{
			return new Model(source);
		}
		public static implicit operator Model(string source)
		{
			return new Model(source);
		}
		public static implicit operator Model(PedHash source)
		{
			return new Model(source);
		}
		public static implicit operator Model(uint source)
		{
			return new Model(source);
		}

		public override int GetHashCode()
		{
			return Hash;
		}

		public override string ToString()
		{
			return "0x" + Hash.ToString("X08");
		}
	}
}
