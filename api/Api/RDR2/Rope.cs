//
// Copyright (C) 2015 crosire & contributors
// License: https://github.com/crosire/scripthookvdotnet#license
//

using RDR2.Math;
using RDR2.Native;
using System;

namespace RDR2
{
	public sealed class Rope : PoolObject
	{
		public Rope(int handle) : base(handle)
		{
			Handle = handle;
		}

		public int VertexCount => PHYSICS.GET_ROPE_VERTEX_COUNT(Handle);
		public bool IsBroken => PHYSICS._IS_ROPE_BROKEN(Handle);

		public float Length
		{
			//get => PHYSICS._0x3D69537039F8D824((ulong)Handle);
			set => PHYSICS.ROPE_FORCE_LENGTH(Handle, value);
		}

		/*public void temp()
		{
			unsafe
			{
				int ptr = Handle;
				PHYSICS._0x7A54D82227A139DB(&ptr, 1); // makes rope (in)visible
			}
			
		}*/

		public void ActivatePhysics()
		{
			PHYSICS.ACTIVATE_PHYSICS(Handle);
		}

		public Vector3 GetVertexCoord(int vertex)
		{
			return PHYSICS.GET_ROPE_VERTEX_COORD(Handle, vertex);
		}

		// TODO
		/*public void AttachEntities(Entity entityOne, Entity entityTwo, float length)
		{
			AttachEntities(entityOne, Vector3.Zero, entityTwo, Vector3.Zero, length);
		}
		public void AttachEntities(Entity entityOne, Vector3 positionOne, Entity entityTwo, Vector3 positionTwo, float length)
		{
			PHYSICS.ATTACH_ENTITIES_TO_ROPE(Handle, entityOne.Handle, entityTwo.Handle, positionOne.X, positionOne.Y, positionOne.Z, positionTwo.X, positionTwo.Y, positionTwo.Z, length, 0, 0, "", "", false, 37709, 7966, 0, 0, true, true);
		}*/

		public bool IsAttachedTo(Entity entity)
		{
			return PHYSICS._IS_ROPE_ATTACHED_TO_ENTITY(Handle, entity.Handle);
		}

		public void Detach(Entity entity)
		{
			PHYSICS.DETACH_ROPE_FROM_ENTITY(Handle, entity.Handle);
		}

		public void Release()
		{
			PHYSICS._RELEASE_ROPE(Handle);
		}

		public override void Delete()
		{
			int handle = Handle;
			unsafe
			{
				PHYSICS.DELETE_ROPE(&handle);
			}
		}

		public override bool Exists()
		{
			return PHYSICS.DOES_ROPE_EXIST(Handle);
		}
		public static bool Exists(Rope rope)
		{
			return rope != null && rope.Exists();
		}

		public bool Equals(Rope obj)
		{
			return !(obj is null) && Handle == obj.Handle;
		}
		public override bool Equals(object obj)
		{
			return !(obj is null) && obj.GetType() == GetType() && Equals((Rope)obj);
		}

		public static bool operator ==(Rope left, Rope right)
		{
			return left is null ? right is null : left.Equals(right);
		}
		public static bool operator !=(Rope left, Rope right)
		{
			return !(left == right);
		}

		public sealed override int GetHashCode()
		{
			return Handle.GetHashCode();
		}
	}
}
