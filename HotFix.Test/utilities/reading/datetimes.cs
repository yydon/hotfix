using System;
using FluentAssertions;
using HotFix.Encoding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HotFix.Test.utilities.reading
{
    [TestClass]
    public class datetimes
    {
        [TestMethod]
        public void timestamp()
        {
            var result = System.Text.Encoding.ASCII.GetBytes("20170327-15:45:13").ReadDateTime();

            result.Should().Be(DateTime.Parse("27/03/2017 15:45:13"));
        }

        [TestMethod]
        public void timestamp_with_milliseconds()
        {
            var result = System.Text.Encoding.ASCII.GetBytes("20170327-15:45:13.596").ReadDateTime();

            result.Should().Be(DateTime.Parse("27/03/2017 15:45:13.596"));
        }
    }
}