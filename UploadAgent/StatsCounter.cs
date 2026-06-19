using System;
using System.Threading;

namespace UploadAgent
{
    /// <summary>
    /// スレッドセーフな本日処理統計カウンタ
    /// 日付が変わると自動リセット
    /// </summary>
    public class StatsCounter
    {
        private int    _movedCount;
        private int    _errorCount;
        private string _currentDate;
        private readonly object _lock = new object();

        public StatsCounter()
        {
            _currentDate = DateTime.Now.ToString("yyyyMMdd");
        }

        public void IncrementMoved(int count = 1)
        {
            lock (_lock) { CheckDayRollover(); _movedCount += count; }
        }

        public void IncrementError(int count = 1)
        {
            lock (_lock) { CheckDayRollover(); _errorCount += count; }
        }

        public (int moved, int error) GetToday()
        {
            lock (_lock) { CheckDayRollover(); return (_movedCount, _errorCount); }
        }

        public void Reset()
        {
            lock (_lock) { _movedCount = 0; _errorCount = 0; }
        }

        private void CheckDayRollover()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (today != _currentDate)
            {
                _movedCount  = 0;
                _errorCount  = 0;
                _currentDate = today;
            }
        }
    }
}
