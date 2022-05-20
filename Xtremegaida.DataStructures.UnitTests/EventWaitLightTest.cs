using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using System.Threading.Tasks;

namespace Xtremegaida.DataStructures.UnitTests
{
   [TestClass]
   public class EventWaitLightTest
   {
      [TestMethod]
      public void BasicSignal()
      {
         var wait = new EventWaitLight();
         wait.Trigger();
         var task = wait.WaitAsync();
         Assert.IsTrue(task.IsCompletedSuccessfully);
      }

      [TestMethod]
      public async Task BasicSignalAfterWait()
      {
         var wait = new EventWaitLight();
         var task = wait.WaitAsync();
         Assert.IsFalse(task.IsCompletedSuccessfully);
         wait.Trigger();
         var timeout = Task.Delay(100);
         var done = await Task.WhenAny(task, timeout);
         Assert.AreNotEqual(timeout, done);
         await done;
      }

      [TestMethod]
      public async Task BasicSignalCancel()
      {
         var wait = new EventWaitLight();
         var cs = new CancellationTokenSource();
         var task = wait.WaitAsync(cs.Token);
         _ = Task.Run(() => cs.CancelAfter(1));
         var cancelled = false;
         var timeout = Task.Delay(100);
         var done = await Task.WhenAny(task, timeout);
         Assert.AreNotEqual(timeout, done);
         try { await done; } catch (TaskCanceledException) { cancelled = true; }
         Assert.IsTrue(cancelled);
      }

      [TestMethod]
      public void ManualResetTrigger()
      {
         var wait = new EventWaitLight();
         wait.Trigger(null, true);
         var task = wait.WaitAsync();
         Assert.IsTrue(task.IsCompletedSuccessfully);
         task = wait.WaitAsync();
         Assert.IsTrue(task.IsCompletedSuccessfully);
         wait.Reset();
         task = wait.WaitAsync();
         Assert.IsFalse(task.IsCompletedSuccessfully);
         wait.Trigger();
      }

      [TestMethod]
      public void FastTrigger()
      {
         var wait = new EventWaitLight();
         var done = false;
         Task.Run(async () =>
         {
            for (int i = 0; i < 100; i++) { await wait.WaitAsync(); }
            done = true;
         });
         int triggers = 0;
         var spin = new SpinWait();
         while (!done)
         {
            if (wait.Triggered) { spin.SpinOnce(); }
            else { wait.Trigger(); triggers++; }
         }
         if (triggers == 101) { triggers = 100; } // Can be +1
         Assert.AreEqual(100, triggers);
      }

      [TestMethod]
      public async Task NoLockRecursion()
      {
         var a = new EventWaitLight();
         var b = new EventWaitLight();
         var done = false;
         _ = Task.Run(async () =>
         {
            for (int tries = 0; tries < 10; tries++)
            {
               a.Trigger();
               await b.WaitAsync();
               b.Reset();
            }
            done = true;
            a.Trigger(null, true);
         });
         while (!done)
         {
            b.Trigger();
            await a.WaitAsync();
            a.Reset();
         }
      }
   }
}

