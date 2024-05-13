using System;
using System.Collections.Generic;
using System.Text;

namespace GizboxLang.Examples
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

		public static String GizboxLang__Examples__Person_get_name(GizboxLang.Examples.Person obj)
		{
			return obj.name;
		}
		public static void GizboxLang__Examples__Person_set_name(GizboxLang.Examples.Person obj, String newv)
		{
			obj.name = newv;
		}
		public static Int32 GizboxLang__Examples__Person_get_age(GizboxLang.Examples.Person obj)
		{
			return obj.age;
		}
		public static void GizboxLang__Examples__Person_set_age(GizboxLang.Examples.Person obj, Int32 newv)
		{
			obj.age = newv;
		}
		public static Vector3 GizboxLang__Examples__Person_get_Position(GizboxLang.Examples.Person obj)
		{
			return obj.Position;
		}
		public static void GizboxLang__Examples__Person_Say(GizboxLang.Examples.Person arg0, System.String arg1)
		{
			arg0.Say(arg1);
		}
		public static Int32 GizboxLang__Examples__Student_get_score(GizboxLang.Examples.Student obj)
		{
			return obj.score;
		}
		public static void GizboxLang__Examples__Student_set_score(GizboxLang.Examples.Student obj, Int32 newv)
		{
			obj.score = newv;
		}
		public static Person GizboxLang__Examples__Student_get_father(GizboxLang.Examples.Student obj)
		{
			return obj.father;
		}
		public static void GizboxLang__Examples__Student_set_father(GizboxLang.Examples.Student obj, Person newv)
		{
			obj.father = newv;
		}
		public static Person GizboxLang__Examples__Student_get_mother(GizboxLang.Examples.Student obj)
		{
			return obj.mother;
		}
		public static void GizboxLang__Examples__Student_set_mother(GizboxLang.Examples.Student obj, Person newv)
		{
			obj.mother = newv;
		}
		public static void GizboxLang__Examples__Student_Say(GizboxLang.Examples.Student arg0, System.String arg1)
		{
			arg0.Say(arg1);
		}
		public static void GizboxLang__Examples__Student_SpeakTo(GizboxLang.Examples.Student arg0, GizboxLang.Examples.Person arg1, System.String arg2)
		{
			arg0.SpeakTo(arg1, arg2);
		}



	}


}
