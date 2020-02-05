using NUnit.Framework;
using Unity.Media.Utilities;

public class FIFOs
{
    [Test]
    public void InitialCondition()
    {
        var fifo = new FIFO(10);

        Assert.AreEqual(10, fifo.FreeLength);
        Assert.AreEqual(10, fifo.TotalLength);
        Assert.AreEqual(10, fifo.FuturedFreeLength);
        Assert.Zero(fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);
    }

    [Test]
    [TestCase(10)]
    [TestCase(19912334)]
    [TestCase(5)]
    [TestCase(11)]
    public void RequestWrite(int fifoSize)
    {
        var fifo = new FIFO(fifoSize);

        var indices = fifo.RequestWrite(4, out var actual);

        Assert.AreEqual(4, actual);

        Assert.AreEqual(fifoSize, fifo.FreeLength);
        Assert.AreEqual(fifoSize, fifo.TotalLength);
        Assert.AreEqual(fifoSize, fifo.FuturedFreeLength);
        Assert.Zero(fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(0, indices[0].Begin);
        Assert.AreEqual(4, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(0, indices[1].End);
    }

    [Test]
    [TestCase(10)]
    [TestCase(19912334)]
    [TestCase(5)]
    [TestCase(11)]
    public void CommitWrite(int fifoSize)
    {
        var fifo = new FIFO(fifoSize);

        fifo.CommitWrite(4);

        Assert.AreEqual(fifoSize-4, fifo.FreeLength);
        Assert.AreEqual(fifoSize, fifo.TotalLength);
        Assert.AreEqual(fifoSize-4, fifo.FuturedFreeLength);
        Assert.AreEqual(4, fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);
    }

    [Test]
    public void EmptyReadRequest()
    {
        var fifo = new FIFO(10);

        var indices = fifo.RequestRead(4, out var actual);

        Assert.Zero(actual);

        Assert.AreEqual(10, fifo.FreeLength);
        Assert.AreEqual(10, fifo.TotalLength);
        Assert.AreEqual(10, fifo.FuturedFreeLength);
        Assert.Zero(fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(0, indices[0].Begin);
        Assert.AreEqual(0, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(0, indices[1].End);
    }

    [Test]
    [TestCase(10)]
    [TestCase(19912334)]
    [TestCase(5)]
    [TestCase(11)]
    public void ReasonableWriteThenRead(int fifoSize)
    {
        var fifo = new FIFO(fifoSize);

        fifo.CommitWrite(5);

        Assert.AreEqual(fifoSize-5, fifo.FreeLength);
        Assert.AreEqual(fifoSize, fifo.TotalLength);
        Assert.AreEqual(fifoSize-5, fifo.FuturedFreeLength);
        Assert.AreEqual(5, fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);

        var indices = fifo.RequestRead(4, out var actual);

        Assert.AreEqual(4, actual);

        Assert.AreEqual(fifoSize-5, fifo.FreeLength);
        Assert.AreEqual(fifoSize, fifo.TotalLength);
        Assert.AreEqual(fifoSize-5, fifo.FuturedFreeLength);
        Assert.AreEqual(5, fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(0, indices[0].Begin);
        Assert.AreEqual(4, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(0, indices[1].End);

        fifo.CommitRead(4);

        Assert.AreEqual(fifoSize-1, fifo.FreeLength);
        Assert.AreEqual(fifoSize, fifo.TotalLength);
        Assert.AreEqual(fifoSize-1, fifo.FuturedFreeLength);
        Assert.AreEqual(1, fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);
    }

    [Test]
    public void UnfulfilledReadRequest()
    {
        var fifo = new FIFO(10);

        fifo.CommitWrite(5);

        Assert.AreEqual(5, fifo.FreeLength);
        Assert.AreEqual(10, fifo.TotalLength);
        Assert.AreEqual(5, fifo.FuturedFreeLength);
        Assert.AreEqual(5, fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);

        var indices = fifo.RequestRead(7, out var actual);

        Assert.AreEqual(5, actual);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(0, indices[0].Begin);
        Assert.AreEqual(5, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(0, indices[1].End);
    }

    [Test]
    [TestCase(10)]
    [TestCase(19912334)]
    [TestCase(11)]
    [TestCase(14)]
    public void UnfulfilledWriteRequest(int fifoSize)
    {
        var fifo = new FIFO(fifoSize);

        fifo.CommitWrite(fifoSize - 7);

        Assert.AreEqual(7, fifo.FreeLength);
        Assert.AreEqual(fifoSize, fifo.TotalLength);
        Assert.AreEqual(7, fifo.FuturedFreeLength);
        Assert.AreEqual(fifoSize - 7, fifo.AvailableLength);
        Assert.Zero(fifo.FuturedWriteLength);

        var indices = fifo.RequestWrite(12, out var actual);

        Assert.AreEqual(7, actual);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(fifoSize - 7, indices[0].Begin);
        Assert.AreEqual(fifoSize, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(0, indices[1].End);
    }

    [Test]
    public void SimpleDoubleRead()
    {
        var fifo = new FIFO(10);

        fifo.CommitWrite(8);

        var indices = fifo.RequestRead(3, out var actual);

        Assert.AreEqual(3, actual);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(0, indices[0].Begin);
        Assert.AreEqual(3, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(0, indices[1].End);

        fifo.CommitRead(3);

        indices = fifo.RequestRead(3, out actual);

        Assert.AreEqual(3, actual);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(3, indices[0].Begin);
        Assert.AreEqual(6, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(0, indices[1].End);
    }

    [Test]
    public void CrossRingWrite()
    {
        var fifo = new FIFO(10);

        fifo.CommitWrite(7);
        fifo.CommitRead(7);

        var indices = fifo.RequestWrite(6, out var actual);

        Assert.AreEqual(6, actual);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(7, indices[0].Begin);
        Assert.AreEqual(10, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(3, indices[1].End);
    }

    [Test]
    public void CrossRingRead()
    {
        var fifo = new FIFO(10);

        fifo.CommitWrite(10);
        fifo.CommitRead(7);
        fifo.CommitWrite(4);

        var indices = fifo.RequestRead(6, out var actual);

        Assert.AreEqual(6, actual);

        Assert.AreEqual(2, indices.RangeCount);
        Assert.AreEqual(7, indices[0].Begin);
        Assert.AreEqual(10, indices[0].End);
        Assert.AreEqual(0, indices[1].Begin);
        Assert.AreEqual(3, indices[1].End);
    }
}
