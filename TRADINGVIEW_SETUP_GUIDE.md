# TradingView to Bot Integration - Quick Setup Guide

This guide walks you through setting up the complete TradingView → Webhook → Bot integration.

## 📋 Prerequisites

- TradingView account (any plan that supports custom alerts)
- Python 3.8+
- ngrok account (free tier works fine)
- Your intelligent_trader.py bot already configured

## 🚀 Quick Start (5 minutes)

### Step 1: Install Dependencies

```bash
cd bot
pip install flask requests
```

### Step 2: Start the Webhook Receiver

```bash
python tradingview_webhook.py
```

You should see:
```
Starting TradingView Webhook Receiver
Starting Flask web server on http://0.0.0.0:5000
Webhook endpoint: http://0.0.0.0:5000/webhook
```

### Step 3: Expose Webhook to Internet

In a new terminal:

```bash
ngrok http 5000
```

Copy the HTTPS URL (e.g., `https://abc123.ngrok.io`)

### Step 4: Add Strategy to TradingView

1. Open TradingView
2. Click Pine Editor (bottom of screen)
3. Copy contents of `pine/combined_strategy.pine`
4. Paste into editor
5. Click "Add to Chart"

### Step 5: Create Alert

1. Click the Alert icon (⏰)
2. Condition: Select "Combined Multi-Indicator Strategy"
3. Choose "Combined Buy Signal" or "Combined Sell Signal"
4. In "Notifications" tab:
   - ✓ Check "Webhook URL"
   - Paste: `https://abc123.ngrok.io/webhook`
5. Click "Create"

### Step 6: Test It!

Send a test webhook:

```bash
curl -X POST http://localhost:5000/webhook \
  -H "Content-Type: application/json" \
  -d '{
    "action": "buy",
    "type": "reversal",
    "price": 50000,
    "time": "2025-11-26 14:30:00",
    "indicators": {
      "supertrend": true,
      "obos": true,
      "trending": true
    }
  }'
```

Check the logs:
```bash
tail -f bot/tradingview_webhook.log
```

## 🔗 Integration with intelligent_trader.py

You have 4 integration options. Choose one:

### Option 1: File-Based (Simplest) ⭐ Recommended for Testing

**In `tradingview_webhook.py`**, replace the TODO in `process_signals()`:

```python
from tradingview_integration_example import write_signal_to_file

def process_signals():
    while not stop_event.is_set():
        try:
            signal = signal_queue.get(timeout=1.0)
            logger.info(f"Processing signal: {signal}")

            # Write to shared file
            write_signal_to_file(signal)

            signal_queue.task_done()
        except queue.Empty:
            continue
```

**In `intelligent_trader.py`**, add to your main loop:

```python
from tradingview_integration_example import read_unprocessed_signals

# In your bot's main loop, periodically check for signals:
signals = read_unprocessed_signals()
for signal in signals:
    if signal['action'] == 'buy':
        # Execute your buy logic
        pass
    elif signal['action'] == 'sell':
        # Execute your sell logic
        pass
```

### Option 2: Async Queue ⭐ Recommended for Real-Time

**In `tradingview_webhook.py`**:

```python
from tradingview_integration_example import queue_tradingview_signal

def process_signals():
    while not stop_event.is_set():
        signal = signal_queue.get(timeout=1.0)
        queue_tradingview_signal(signal.to_dict())
        signal_queue.task_done()
```

**In `intelligent_trader.py`**:

```python
from tradingview_integration_example import tradingview_signal_handler

async def main():
    bot = YourBotClass()
    await asyncio.gather(
        bot.run(),
        tradingview_signal_handler(bot)
    )
```

### Option 3: Direct Execution ⭐ Recommended for Production

**In `tradingview_webhook.py`**:

```python
from tradingview_integration_example import TradingViewSignalExecutor
# Import your bot class
from intelligent_trader import YourBotClass

# Create bot instance
bot = YourBotClass()
executor = TradingViewSignalExecutor(bot)

def process_signals():
    while not stop_event.is_set():
        signal = signal_queue.get(timeout=1.0)
        executor.execute(signal.to_dict())
        signal_queue.task_done()
```

### Option 4: Redis Queue (Enterprise)

Requires Redis server. Best for multiple bots or distributed systems.

## 📊 Understanding the Signals

### Signal Structure

```json
{
  "action": "buy",           // "buy" or "sell"
  "type": "reversal",        // "reversal", "continuation", or "trend"
  "price": 50123.45,         // Current price
  "time": "2025-11-26 14:30:00",
  "indicators": {
    "supertrend": true,      // Which indicators triggered
    "obos": true,
    "trending": true
  }
}
```

### Signal Types Explained

| Type | Description | Risk | Suggested Position Size |
|------|-------------|------|------------------------|
| **Reversal** | Market reversing direction | Higher | 50-70% of normal |
| **Continuation** | Trend continuing after pullback | Lower | 100-150% of normal |
| **Trend** | General trend following | Medium | 100% of normal |

### Recommended Actions by Signal Type

```python
def handle_tradingview_signal(signal):
    action = signal['action']
    signal_type = signal['type']

    if signal_type == 'reversal':
        # Higher risk - use tighter stops
        stop_loss_multiplier = 0.8
        position_size = 1

    elif signal_type == 'continuation':
        # Higher confidence - can be more aggressive
        stop_loss_multiplier = 1.2
        position_size = 2

    else:  # trend
        # Standard approach
        stop_loss_multiplier = 1.0
        position_size = 1

    if action == 'buy':
        execute_buy(position_size, stop_loss_multiplier)
    else:
        execute_sell(position_size, stop_loss_multiplier)
```

## 🎛️ Strategy Configuration

### Conservative (Fewer, Higher Quality Signals)

In TradingView Pine Script settings:
- Swing Length: 20
- Trade Trigger Length: 15
- Risk to Reward Threshold: 1.5
- Enable all 3 indicators

### Balanced (Default)

- Swing Length: 15
- Trade Trigger Length: 10
- Risk to Reward Threshold: 1.0
- Enable all 3 indicators

### Aggressive (More Frequent Signals)

- Swing Length: 10
- Trade Trigger Length: 5
- Risk to Reward Threshold: 0.5
- Use only Supertrend + OB/OS

## 🔧 Troubleshooting

### Webhooks Not Received

1. **Check ngrok is running:**
   ```bash
   # You should see "Session Status: online"
   ngrok http 5000
   ```

2. **Verify webhook URL:**
   - Must be HTTPS (ngrok provides this)
   - Must end with `/webhook`
   - Example: `https://abc123.ngrok.io/webhook`

3. **Test manually:**
   ```bash
   curl -X POST https://abc123.ngrok.io/webhook \
     -H "Content-Type: application/json" \
     -d '{"action":"buy","type":"trend","price":50000}'
   ```

4. **Check logs:**
   ```bash
   tail -f bot/tradingview_webhook.log
   ```

### Strategy Not Generating Signals

1. **Check indicator settings:**
   - All 3 indicators enabled?
   - Sufficient historical data loaded?

2. **Test on different timeframe:**
   - Try 5m, 15m, 1h charts
   - Lower timeframes = more signals

3. **Reduce minimum consensus:**
   - Edit Pine Script, change `min_signals = 2` to `1`

### Duplicate Signals

The webhook receiver automatically filters duplicates. If you still get them:

1. Check TradingView alert frequency setting (should be "Once Per Bar Close")
2. Increase `MAX_RECENT_SIGNALS` in `tradingview_webhook.py`

## 📈 Monitoring & Logs

### Check Webhook Health
```bash
curl http://localhost:5000/health
```

Response:
```json
{
  "status": "healthy",
  "timestamp": "2025-11-26T14:30:00",
  "queue_size": 0
}
```

### View Statistics
```bash
curl http://localhost:5000/stats
```

### Monitor Logs in Real-Time
```bash
tail -f bot/tradingview_webhook.log
```

### Check ngrok Traffic
Visit `http://localhost:4040` (ngrok web interface)

## 🔒 Security Considerations

1. **Add Authentication:**
   ```python
   # In tradingview_webhook.py
   SECRET_TOKEN = "your-secret-token"

   @app.route('/webhook', methods=['POST'])
   def webhook():
       auth_header = request.headers.get('Authorization')
       if auth_header != f"Bearer {SECRET_TOKEN}":
           return jsonify({'error': 'Unauthorized'}), 401
       # ... rest of code
   ```

   In TradingView alert, add to webhook URL:
   ```
   https://abc123.ngrok.io/webhook
   Headers: Authorization: Bearer your-secret-token
   ```

2. **Rate Limiting:**
   Install: `pip install flask-limiter`
   ```python
   from flask_limiter import Limiter

   limiter = Limiter(app, default_limits=["100 per minute"])
   ```

3. **HTTPS Only:**
   - ngrok provides HTTPS
   - For production, use proper SSL certificate

## 📱 Production Deployment

For 24/7 operation:

### Option 1: Cloud VM (Recommended)
- Deploy to AWS EC2, Google Cloud, or DigitalOcean
- Use systemd to run as service
- Use nginx as reverse proxy (instead of ngrok)

### Option 2: Docker
```dockerfile
FROM python:3.10-slim
WORKDIR /app
COPY requirements.txt .
RUN pip install -r requirements.txt
COPY bot/ ./bot/
CMD ["python", "bot/tradingview_webhook.py"]
```

### Option 3: Process Manager
```bash
# Install PM2
npm install -g pm2

# Start webhook
pm2 start "python bot/tradingview_webhook.py" --name webhook

# Start on boot
pm2 startup
pm2 save
```

## 📚 Next Steps

1. ✅ Test with paper trading first
2. ✅ Monitor for 24-48 hours
3. ✅ Adjust strategy parameters based on results
4. ✅ Add position sizing logic
5. ✅ Implement risk management
6. ✅ Go live with small position sizes

## 🆘 Support & Resources

- **Pine Script Docs:** https://www.tradingview.com/pine-script-docs/
- **Flask Docs:** https://flask.palletsprojects.com/
- **ngrok Docs:** https://ngrok.com/docs
- **Project Files:**
  - Strategy: `pine/combined_strategy.pine`
  - Webhook: `bot/tradingview_webhook.py`
  - Integration: `bot/tradingview_integration_example.py`
  - Strategy Docs: `pine/COMBINED_STRATEGY_README.md`

## 💡 Tips

1. **Start Conservative:** Use higher risk/reward thresholds initially
2. **Test Thoroughly:** Run for a week on paper trading
3. **Monitor Closely:** Check logs daily for the first week
4. **Adjust Gradually:** Make small tweaks, measure impact
5. **Keep Records:** Log all signals and outcomes for analysis
