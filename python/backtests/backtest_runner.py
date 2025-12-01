"""CSV-based backtest runner for IntelligentSignalTrader.

Usage:
    python python/backtests/backtest_runner.py --data "C:/Users/Magwe/Work/Trading_Apps/data_collector/data/ohlcv/futures/Charts - MNQ.CME.csv"
"""

from __future__ import annotations

import argparse
import asyncio
from dataclasses import dataclass, field
from pathlib import Path
import sys
from types import SimpleNamespace
from typing import List, Optional

import pandas as pd

REPO_ROOT = Path(__file__).resolve().parents[2]
if str(REPO_ROOT) not in sys.path:
    sys.path.append(str(REPO_ROOT))

from originals.intelligent_signal_trader_gpt import IntelligentSignalTrader


class BacktestClient:
    """Minimal NT8 client stub used during backtests."""

    def __init__(self, starting_cash: float = 10_000.0):
        self.starting_cash = starting_cash

    # --- Connectivity & market data -------------------------------------------------
    def unsubscribe_market_data(self, symbol: str) -> None:  # pragma: no cover - trivial stub
        return None

    def subscribe_market_data(self, symbol: str) -> None:  # pragma: no cover - trivial stub
        return None

    # --- Account / portfolio access -------------------------------------------------
    def get_account_balance(self, account_id: str):
        balance = SimpleNamespace(cash_balance=self.starting_cash, open_trade_equity=0.0)
        return SimpleNamespace(balances=[balance])

    def get_positions(self, account_id: Optional[str] = None):
        return SimpleNamespace(positions=[])

    def get_orders(self, account_id: Optional[str] = None):
        return SimpleNamespace(orders=[])

    # --- Trading operations (not used in offline sim but kept for interface safety) --
    def cancel_order(self, account_id: str, order_id: str) -> bool:
        return True

    def place_order(self, *_, **__):  # pragma: no cover - debug stub
        return SimpleNamespace(order_id="backtest-order")

    def close_position(self, account_id: str, symbol: str) -> bool:  # pragma: no cover
        return True


@dataclass
class TradeRecord:
    direction: str
    entry_time: pd.Timestamp
    exit_time: pd.Timestamp
    entry_price: float
    exit_price: float
    quantity: int
    pnl_points: float
    pnl_dollars: float


@dataclass
class BacktestMetrics:
    trades: List[TradeRecord] = field(default_factory=list)
    realized_pnl: float = 0.0
    wins: int = 0
    losses: int = 0

    @property
    def total_trades(self) -> int:
        return len(self.trades)

    @property
    def win_rate(self) -> float:
        return (self.wins / self.total_trades) * 100 if self.total_trades else 0.0

    def add_trade(self, trade: TradeRecord) -> None:
        self.trades.append(trade)
        self.realized_pnl += trade.pnl_dollars
        if trade.pnl_dollars >= 0:
            self.wins += 1
        else:
            self.losses += 1


class CSVBacktestRunner:
    """Simple CSV replay harness for IntelligentSignalTrader."""

    def __init__(
        self,
        data_path: Path,
        account_id: str = "Backtest",
        symbol: str = "MNQ",
        is_propfirm: bool = False,
        warmup_bars: int = 150,
        contracts: int = 1,
    ) -> None:
        self.data_path = data_path
        self.warmup_bars = warmup_bars
        self.contracts = contracts
        self.df = self._load_csv()
        self.client = BacktestClient()
        self.trader = IntelligentSignalTrader(
            account_id=account_id,
            symbol=symbol,
            client=self.client,
            use_rithmic_data=False,
            is_propfirm=is_propfirm,
        )
        self.trader.market_data_cache = []  # ensure clean state
        self.trader.mtf_data_cache = {tf: [] for tf in ['1m', '15m', '30m', '60m']}
        self.metrics = BacktestMetrics()
        self.position_side = 0  # -1 short, 0 flat, 1 long
        self.entry_price = None
        self.entry_time = None

    def _load_csv(self) -> pd.DataFrame:
        if not self.data_path.exists():
            raise FileNotFoundError(f"Data file not found: {self.data_path}")

        df = pd.read_csv(self.data_path)
        rename_map = {
            'Bar Ending Time': 'timestamp',
            'Series.Open': 'open',
            'Series.High': 'high',
            'Series.Low': 'low',
            'Series.Close': 'close',
            'Series.Volume': 'volume',
        }
        df = df.rename(columns=rename_map)
        df = df.dropna(subset=['timestamp', 'open', 'high', 'low', 'close'])
        df['timestamp'] = pd.to_datetime(df['timestamp'])
        if df['timestamp'].dt.tz is None:
            df['timestamp'] = df['timestamp'].dt.tz_localize('America/New_York')
        df = df.sort_values('timestamp').reset_index(drop=True)
        for col in ['open', 'high', 'low', 'close', 'volume']:
            df[col] = pd.to_numeric(df[col], errors='coerce')
        df = df.dropna(subset=['open', 'high', 'low', 'close']).reset_index(drop=True)
        return df

    async def run(self) -> BacktestMetrics:
        point_value = self.trader.point_value

        for idx in range(len(self.df)):
            row = self.df.iloc[idx]
            bar = {
                'timestamp': row['timestamp'],
                'open': float(row['open']),
                'high': float(row['high']),
                'low': float(row['low']),
                'close': float(row['close']),
                'volume': int(row.get('volume', 0) or 0),
                'tick_count': 1,
            }
            self._ingest_bar(bar)

            if idx < self.warmup_bars:
                continue

            df_ind = await self.trader.build_indicator_dataframe()
            if df_ind is None or df_ind.empty or len(df_ind) < 30:
                continue

            signal_details = await self.trader.get_signal_with_details()
            signal = signal_details.get('signal', 'HOLD')
            await self._process_signal(signal, bar, point_value)

        # Close any remaining position at last close
        if self.position_side != 0 and self.entry_price is not None:
            last_bar = self.df.iloc[-1]
            await self._close_position(
                exit_price=float(last_bar['close']),
                exit_time=last_bar['timestamp'],
                point_value=point_value,
            )

        return self.metrics

    def _ingest_bar(self, bar: dict) -> None:
        self.trader.market_data_cache.append(bar)
        if len(self.trader.market_data_cache) > self.trader.max_cache_size:
            self.trader.market_data_cache.pop(0)
        # Keep MTF caches fresh for ADX calculations
        try:
            self.trader.aggregate_to_1min_for_mtf(bar)
        except Exception:
            pass

    async def _process_signal(self, signal: str, bar: dict, point_value: float) -> None:
        price = bar['close']
        timestamp = bar['timestamp']

        if signal == 'BUY':
            if self.position_side == -1:
                await self._close_position(price, timestamp, point_value)
            if self.position_side == 0:
                self._open_position(1, price, timestamp)
        elif signal == 'SELL':
            if self.position_side == 1:
                await self._close_position(price, timestamp, point_value)
            if self.position_side == 0:
                self._open_position(-1, price, timestamp)
        # HOLD leaves the current position untouched

    def _open_position(self, side: int, price: float, timestamp: pd.Timestamp) -> None:
        self.position_side = side
        self.entry_price = price
        self.entry_time = timestamp
        direction = 'LONG' if side == 1 else 'SHORT'
        print(f"[{timestamp}] OPEN {direction} @ {price:.2f}")

    async def _close_position(self, exit_price: float, exit_time: pd.Timestamp, point_value: float) -> None:
        if self.position_side == 0 or self.entry_price is None or self.entry_time is None:
            return

        direction = 'LONG' if self.position_side == 1 else 'SHORT'
        multiplier = self.contracts * point_value
        pnl_points = (exit_price - self.entry_price) * self.position_side
        pnl_dollars = pnl_points * multiplier

        trade = TradeRecord(
            direction=direction,
            entry_time=self.entry_time,
            exit_time=exit_time,
            entry_price=self.entry_price,
            exit_price=exit_price,
            quantity=self.contracts,
            pnl_points=pnl_points,
            pnl_dollars=pnl_dollars,
        )
        self.metrics.add_trade(trade)

        print(
            f"[{exit_time}] CLOSE {direction} @ {exit_price:.2f} | "
            f"PnL: {pnl_points:+.2f} pts (${pnl_dollars:+.2f})"
        )

        # Reset position state
        self.position_side = 0
        self.entry_price = None
        self.entry_time = None


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Backtest runner for IntelligentSignalTrader")
    parser.add_argument(
        "--data",
        type=Path,
        default=Path(r"C:/Users/Magwe/Work/Trading_Apps/data_collector/data/ohlcv/futures/Charts - MNQ.CME.csv"),
        help="Path to CSV file containing OHLCV data",
    )
    parser.add_argument("--symbol", default="MNQ", help="Symbol to use for the trader")
    parser.add_argument("--account", default="Backtest", help="Account identifier for the trader")
    parser.add_argument("--warmup", type=int, default=150, help="Number of bars to warm up indicators")
    parser.add_argument("--contracts", type=int, default=1, help="Number of contracts per trade")
    return parser.parse_args()


async def main_async(args: argparse.Namespace) -> None:
    runner = CSVBacktestRunner(
        data_path=args.data,
        account_id=args.account,
        symbol=args.symbol,
        warmup_bars=args.warmup,
        contracts=args.contracts,
    )
    metrics = await runner.run()

    print("\n=== Backtest Summary ===")
    print(f"Trades executed : {metrics.total_trades}")
    print(f"Wins / Losses   : {metrics.wins} / {metrics.losses}")
    print(f"Win rate        : {metrics.win_rate:.2f}%")
    print(f"Realized PnL    : ${metrics.realized_pnl:,.2f}")
    if metrics.total_trades:
        avg = metrics.realized_pnl / metrics.total_trades
        print(f"Avg trade PnL   : ${avg:,.2f}")


def main() -> None:
    args = parse_args()
    asyncio.run(main_async(args))


if __name__ == "__main__":
    main()
