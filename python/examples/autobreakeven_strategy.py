"""
Auto-Breakeven Strategy Example - NinjaTrader 8 Python SDK

This strategy demonstrates the Auto-Breakeven functionality with predefined offsets:
- BE1: ±5 ticks (conservative breakeven)
- BE2: ±8 ticks (moderate breakeven)
- BE3: ±12 ticks (extended breakeven)

The strategy will:
1. Place initial positions
2. Monitor positions and set auto-breakeven when profitable
3. Track position changes and breakeven levels
4. Display comprehensive status information
"""

import sys
import os
import time
import threading
from datetime import datetime

# Add the parent directory to Python path for imports
sys.path.append(os.path.join(os.path.dirname(__file__), '..'))

from nt8.client_filebased import NT8Client
from nt8.types import OrderType, OrderAction, TimeInForce

class AutoBreakevenStrategy:
    def __init__(self):
        self.client = NT8Client()
        self.running = False
        self.positions = {}
        self.breakeven_status = {}
        
        # Auto-Breakeven Configuration
        self.be_offsets = {
            'BE1': 5,   # Conservative breakeven at +5 ticks
            'BE2': 8,   # Moderate breakeven at +8 ticks  
            'BE3': 12   # Extended breakeven at +12 ticks
        }
        
        # Strategy parameters
        self.instrument = "ES 12-24"
        self.quantity = 1
        self.monitor_interval = 2.0  # seconds
        
    def connect(self):
        """Connect to NT8 and verify connection"""
        print("🔌 Connecting to NinjaTrader 8...")
        
        # Test connection with PING  
        try:
            response = self.client.ping()
            if not ('OK' in response or 'PONG' in response):
                print(f"❌ NT8 not responding: {response}")
                return False
        except Exception as e:
            print(f"❌ Failed to connect to NT8: {e}")
            return False
            
        print("✅ Connected to NinjaTrader 8 successfully")
        return True
        
    def get_account_info(self):
        """Display account information"""
        print("\n📊 Account Information:")
        accounts = self.client.get_accounts()
        if accounts:
            for account in accounts:
                print(f"  Account: {account['name']} | Balance: ${account.get('cash_value', 'N/A')}")
        else:
            print("  No accounts found")
            
    def display_positions(self):
        """Display current positions and P&L"""
        positions = self.client.get_positions()
        if not positions:
            print("📍 No open positions")
            return
            
        print("\n📍 Current Positions:")
        for pos in positions:
            pnl = float(pos.get('unrealized_pnl', 0))
            pnl_color = "🟢" if pnl > 0 else "🔴" if pnl < 0 else "⚪"
            
            print(f"  {pos['instrument']} | {pos['position']} {pos['quantity']} @ ${pos['avg_price']}")
            print(f"    P&L: {pnl_color} ${pnl:.2f}")
            
            # Show breakeven status if set
            if pos['instrument'] in self.breakeven_status:
                be_info = self.breakeven_status[pos['instrument']]
                print(f"    🎯 Breakeven: {be_info['level']} @ ${be_info['price']:.2f}")
                
    def place_test_position(self, side="BUY"):
        """Place a test position to demonstrate auto-breakeven"""
        action = OrderAction.BUY if side == "BUY" else OrderAction.SELL
        
        print(f"\n📤 Placing {side} order for {self.quantity} {self.instrument}...")
        
        try:
            result = self.client.place_order(
                account="Sim101",  # Default sim account
                instrument=self.instrument,
                action=side,
                quantity=self.quantity,
                order_type="MARKET"
            )
            
            if result:
                print(f"✅ {side} order submitted successfully")
                return True
            else:
                print(f"❌ Failed to submit {side} order: {result}")
                return False
                
        except Exception as e:
            print(f"❌ Error placing {side} order: {e}")
            return False
            
    def set_auto_breakeven(self, instrument, level="BE1"):
        """Set auto-breakeven for a position"""
        if level not in self.be_offsets:
            print(f"❌ Invalid breakeven level: {level}")
            return False
            
        offset = self.be_offsets[level]
        print(f"\n🎯 Setting Auto-Breakeven {level} (±{offset} ticks) for {instrument}...")
        
        try:
            result = self.client.set_auto_breakeven(
                instrument=instrument,
                be1_offset=offset if level == "BE1" else self.be_offsets["BE1"],
                be2_offset=offset if level == "BE2" else self.be_offsets["BE2"],
                be3_offset=offset if level == "BE3" else self.be_offsets["BE3"]
            )
            
            if result and 'OK' in result:
                print(f"✅ Auto-Breakeven {level} set successfully")
                print(f"   Result: {result}")
                
                # Store breakeven status
                self.breakeven_status[instrument] = {
                    'level': level,
                    'offset': offset,
                    'price': self._extract_breakeven_price(result, level)
                }
                return True
            else:
                print(f"❌ Failed to set auto-breakeven: {result}")
                return False
                
        except Exception as e:
            print(f"❌ Error setting auto-breakeven: {e}")
            return False
            
    def _extract_breakeven_price(self, result_string, level):
        """Extract breakeven price from result string"""
        try:
            # Parse result string like "OK|Breakeven set: Entry=5025.00, BE1=5030.00, BE2=5033.00, BE3=5037.00"
            if "Breakeven set:" in result_string:
                parts = result_string.split("Breakeven set:")[1].strip()
                level_map = {"BE1": "BE1=", "BE2": "BE2=", "BE3": "BE3="}
                
                if level in level_map:
                    search_str = level_map[level]
                    start_idx = parts.find(search_str)
                    if start_idx >= 0:
                        start_idx += len(search_str)
                        end_idx = parts.find(",", start_idx)
                        if end_idx == -1:
                            end_idx = len(parts)
                        return float(parts[start_idx:end_idx].strip())
            return 0.0
        except:
            return 0.0
            
    def monitor_positions(self):
        """Monitor positions and auto-breakeven opportunities"""
        print(f"\n👀 Starting position monitoring (checking every {self.monitor_interval}s)")
        print("   Will automatically set breakeven levels when positions become profitable...")
        
        consecutive_no_positions = 0
        max_no_positions = 10  # Stop after 20 seconds with no positions
        
        while self.running:
            try:
                positions = self.client.get_positions()
                
                if not positions:
                    consecutive_no_positions += 1
                    if consecutive_no_positions >= max_no_positions:
                        print("⏰ No positions found for extended period. Stopping monitor.")
                        break
                    time.sleep(self.monitor_interval)
                    continue
                    
                consecutive_no_positions = 0
                
                for pos in positions:
                    instrument = pos['instrument']
                    pnl = float(pos.get('unrealized_pnl', 0))
                    side = pos['position']
                    
                    # Check if we should set auto-breakeven
                    if instrument not in self.breakeven_status and pnl > 0:
                        print(f"\n💰 Position profitable! PnL: ${pnl:.2f}")
                        
                        # Determine which breakeven level to use based on profit
                        if pnl >= 50:  # Significant profit
                            self.set_auto_breakeven(instrument, "BE3")
                        elif pnl >= 25:  # Moderate profit
                            self.set_auto_breakeven(instrument, "BE2")
                        elif pnl >= 10:  # Small profit
                            self.set_auto_breakeven(instrument, "BE1")
                            
                # Display current status
                self.display_positions()
                print(f"⏰ {datetime.now().strftime('%H:%M:%S')} - Monitoring...")
                
            except Exception as e:
                print(f"❌ Error in monitoring: {e}")
                
            time.sleep(self.monitor_interval)
            
    def run_strategy(self):
        """Run the complete auto-breakeven strategy demonstration"""
        print("🚀 Auto-Breakeven Strategy Starting")
        print("=" * 50)
        
        # Connect to NT8
        if not self.connect():
            return
            
        # Display account info
        self.get_account_info()
        
        # Display initial positions
        print("\n📊 Initial Status:")
        self.display_positions()
        
        # Ask user what to do
        print("\n🎮 Strategy Options:")
        print("  1. Place BUY position and monitor")
        print("  2. Place SELL position and monitor") 
        print("  3. Just monitor existing positions")
        print("  4. Test manual breakeven on existing position")
        print("  5. Exit")
        
        try:
            choice = input("\nSelect option (1-5): ").strip()
            
            if choice == "1":
                if self.place_test_position("BUY"):
                    self.start_monitoring()
            elif choice == "2":
                if self.place_test_position("SELL"):
                    self.start_monitoring()
            elif choice == "3":
                self.start_monitoring()
            elif choice == "4":
                self.test_manual_breakeven()
            elif choice == "5":
                print("👋 Exiting strategy")
                return
            else:
                print("❌ Invalid choice")
                return
                
        except KeyboardInterrupt:
            print("\n⏸️  Strategy interrupted by user")
        except Exception as e:
            print(f"❌ Strategy error: {e}")
        finally:
            self.cleanup()
            
    def start_monitoring(self):
        """Start position monitoring in a separate thread"""
        self.running = True
        
        # Start monitoring thread
        monitor_thread = threading.Thread(target=self.monitor_positions)
        monitor_thread.daemon = True
        monitor_thread.start()
        
        print("\n⌨️  Press Ctrl+C to stop monitoring...")
        
        try:
            # Keep main thread alive
            while self.running:
                time.sleep(1)
        except KeyboardInterrupt:
            print("\n⏸️  Stopping monitoring...")
            self.running = False
            
        # Wait for monitor thread to finish
        monitor_thread.join(timeout=5)
        
    def test_manual_breakeven(self):
        """Test manual breakeven setting on existing positions"""
        positions = self.client.get_positions()
        if not positions:
            print("❌ No positions found to test breakeven")
            return
            
        print("\n📍 Available Positions:")
        for i, pos in enumerate(positions):
            print(f"  {i+1}. {pos['instrument']} - {pos['position']} {pos['quantity']}")
            
        try:
            choice = int(input("\nSelect position (number): ")) - 1
            if 0 <= choice < len(positions):
                pos = positions[choice]
                
                print(f"\n🎯 Available Breakeven Levels for {pos['instrument']}:")
                print(f"  1. BE1 (±{self.be_offsets['BE1']} ticks)")
                print(f"  2. BE2 (±{self.be_offsets['BE2']} ticks)")
                print(f"  3. BE3 (±{self.be_offsets['BE3']} ticks)")
                
                be_choice = int(input("\nSelect breakeven level (1-3): "))
                be_levels = ["BE1", "BE2", "BE3"]
                
                if 1 <= be_choice <= 3:
                    level = be_levels[be_choice - 1]
                    self.set_auto_breakeven(pos['instrument'], level)
                else:
                    print("❌ Invalid breakeven level")
            else:
                print("❌ Invalid position selection")
                
        except (ValueError, IndexError):
            print("❌ Invalid input")
            
    def cleanup(self):
        """Cleanup resources"""
        self.running = False
        print("🧹 Strategy cleanup completed")

def main():
    """Main function to run the auto-breakeven strategy"""
    print("NinjaTrader 8 Auto-Breakeven Strategy")
    print("=====================================")
    print("Breakeven Levels:")
    print("  • BE1: ±5 ticks (Conservative)")
    print("  • BE2: ±8 ticks (Moderate)")  
    print("  • BE3: ±12 ticks (Extended)")
    print()
    
    strategy = AutoBreakevenStrategy()
    strategy.run_strategy()

if __name__ == "__main__":
    main()