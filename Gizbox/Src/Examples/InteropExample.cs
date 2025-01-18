using System;
using System.Collections.Generic;
using System.Text;

namespace Gizbox.Examples
{
    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
    }
    public class Person
    {
        public string name;
        public int age;

        private Vector3 position;
        public Vector3 Position => position;

        public virtual void Say(string txt)
        {
        }
    }
    public class Student : Person
    {
        public int score;
        public Person father;
        public Person mother;

        public override void Say(string txt)
        {
        }
        public void SpeakTo(Person person, string txt)
        {
        }
    }


    public class ExampleInterop
    {

		public static String Gizbox__Examples__Person_get_name(Gizbox.Examples.Person obj)
		{
			return obj.name;
		}
		public static void Gizbox__Examples__Person_set_name(Gizbox.Examples.Person obj, String newv)
		{
			obj.name = newv;
		}
		public static Int32 Gizbox__Examples__Person_get_age(Gizbox.Examples.Person obj)
		{
			return obj.age;
		}
		public static void Gizbox__Examples__Person_set_age(Gizbox.Examples.Person obj, Int32 newv)
		{
			obj.age = newv;
		}
		public static Vector3 Gizbox__Examples__Person_get_Position(Gizbox.Examples.Person obj)
		{
			return obj.Position;
		}
		public static void Gizbox__Examples__Person_Say(Gizbox.Examples.Person arg0, System.String arg1)
		{
			arg0.Say(arg1);
		}
		public static Int32 Gizbox__Examples__Student_get_score(Gizbox.Examples.Student obj)
		{
			return obj.score;
		}
		public static void Gizbox__Examples__Student_set_score(Gizbox.Examples.Student obj, Int32 newv)
		{
			obj.score = newv;
		}
		public static Person Gizbox__Examples__Student_get_father(Gizbox.Examples.Student obj)
		{
			return obj.father;
		}
		public static void Gizbox__Examples__Student_set_father(Gizbox.Examples.Student obj, Person newv)
		{
			obj.father = newv;
		}
		public static Person Gizbox__Examples__Student_get_mother(Gizbox.Examples.Student obj)
		{
			return obj.mother;
		}
		public static void Gizbox__Examples__Student_set_mother(Gizbox.Examples.Student obj, Person newv)
		{
			obj.mother = newv;
		}
		public static void Gizbox__Examples__Student_Say(Gizbox.Examples.Student arg0, System.String arg1)
		{
			arg0.Say(arg1);
		}
		public static void Gizbox__Examples__Student_SpeakTo(Gizbox.Examples.Student arg0, Gizbox.Examples.Person arg1, System.String arg2)
		{
			arg0.SpeakTo(arg1, arg2);
		}



	}


}
