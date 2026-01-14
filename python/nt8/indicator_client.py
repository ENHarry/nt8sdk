import pandas as pd
import os
import time
import logging
import socket
import threading
from pathlib import Path
from typing import Optional, Dict

logger = logging.getLogger(__name__)

class NT8IndicatorClient:
    """
    Client for receiving indicator data from NT8 NTPythonIndicatorExporter strategy.
    
    Primary: TCP streaming (port 50005) - lowest latency
    Fallback: File-based (outgoing/python/indicator_data.txt)
    """
    
    def __init__(self, tcp_port: int = 50005, host: str = '127.0.0.1', documents_dir: Optional[str] = None):
        self.tcp_port = tcp_port
        self.host = host
        
        # Setup file path - check multiple possible locations
        if documents_dir is None:
            documents_dir = os.path.expanduser("~/Documents")
        
        # Primary location: NT8_Python_Data (where strategy writes by default)
        self.primary_dir = Path(documents_dir) / "NT8_Python_Data"
        self.primary_file = self.primary_dir / "nt8_indicators.csv"
        
        # Fallback location: NinjaTrader 8/outgoing/python
        nt_dir = Path(documents_dir) / "NinjaTrader 8"
        self.outgoing_dir = nt_dir / "outgoing" / "python"
        self.indicator_file = self.outgoing_dir / "indicator_data.txt"
        
        # State
        self.latest_data: Optional[Dict[str, float]] = None
        self.last_update_time = 0
        self.tcp_connected = False
        
        # File monitoring state
        self._last_file_mtime = 0
        
        # Column mapping
        self.columns = [
            "Time", "Open", "High", "Low", "Close", "Volume",
            "RSI", "MACD", "MACD_Signal", "MACD_Hist",
            "StochK", "StochD", "ADX", "DI_Plus", "DI_Minus", "ATR",
            "BB_Upper", "BB_Middle", "BB_Lower",
            "HA_Open", "HA_High", "HA_Low", "HA_Close",
            "Trend_Signal", "Div_Signal", "Liq_High", "Liq_Low",
            "RSI_5m", "MACD_5m", "MACD_Signal_5m", "MACD_Hist_5m", "Trend_5m",
            "RSI_15m", "MACD_15m", "MACD_Signal_15m", "MACD_Hist_15m", "Trend_15m"
        ]
        
        # Start TCP Loop
        self._stop_event = threading.Event()
        self._tcp_thread = threading.Thread(target=self._tcp_monitor_loop, daemon=True)
        self._tcp_thread.start()

    def _tcp_monitor_loop(self):
        """
        Background thread to maintain TCP connection and read streaming data.
        """
        while not self._stop_event.is_set():
            try:
                # Attempt Connection
                with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
                    s.settimeout(5.0) # Connection timeout
                    try:
                        s.connect((self.host, self.tcp_port))
                        self.tcp_connected = True
                        logger.info(f"✅ Connected to NT8 Indicator TCP Server on port {self.tcp_port}")
                        
                        # Set longer timeout for reading
                        s.settimeout(None) 
                        file_obj = s.makefile('r', encoding='utf-8')
                        
                        for line in file_obj:
                            if self._stop_event.is_set(): break
                            if not line.strip(): continue
                            
                            self._parse_indicator_line(line.strip())
                                
                    except (socket.error, ConnectionRefusedError) as e:
                        pass  # Silent retry
            except Exception as e:
                logger.error(f"TCP Loop Error: {e}")
                
            self.tcp_connected = False
            time.sleep(2)  # Retry delay
    
    def _parse_indicator_line(self, line: str) -> Optional[Dict[str, float]]:
        """Parse a CSV line into indicator dictionary."""
        parts = line.split(',')
        if len(parts) < len(self.columns):
            return None
        
        data = {}
        for i, col in enumerate(self.columns):
            if i < len(parts):
                try:
                    if col == "Time": 
                        continue
                    data[col] = float(parts[i])
                except ValueError: 
                    continue
        
        self.latest_data = data
        self.last_update_time = time.time()
        return data

    def get_latest_indicators(self) -> Optional[Dict[str, float]]:
        """
        Get the latest indicator values.
        Prioritizes TCP stream (LOW LATENCY).
        Falls back to file-based if TCP is disconnected.
        """
        # 1. Check TCP Freshness (received within last 2 seconds)
        if self.tcp_connected and (time.time() - self.last_update_time < 2.0):
            if self.latest_data:
                return self.latest_data
        
        # 2. File-based read from outgoing/python/indicator_data.txt
        return self._read_indicator_file()

    def _read_indicator_file(self) -> Optional[Dict[str, float]]:
        """
        Reads indicator data from the standard NT8 outgoing directory.
        Uses atomic file pattern for consistency.
        Checks multiple possible locations.
        """
        try:
            # Check primary location first (NT8_Python_Data/nt8_indicators.csv)
            target_file = None
            if self.primary_file.exists():
                target_file = self.primary_file
            elif self.indicator_file.exists():
                target_file = self.indicator_file
            
            if target_file is None:
                return self.latest_data  # Return last known data
            
            # Check if file was modified
            mtime = target_file.stat().st_mtime
            if mtime <= self._last_file_mtime:
                return self.latest_data  # No change, return cached
            
            self._last_file_mtime = mtime

            # Read file - expects header + single data line (or just CSV data)
            content = target_file.read_text()
            lines = content.strip().split('\n')
            
            if len(lines) < 1:
                return self.latest_data
            
            # Parse the data line (last non-empty line with data)
            data_line = lines[-1]
            return self._parse_indicator_line(data_line)

        except Exception as e:
            logger.error(f"Error reading indicator file: {e}")
            return self.latest_data
            
    def close(self):
        self._stop_event.set()
        if self._tcp_thread.is_alive():
            self._tcp_thread.join(timeout=1.0)
