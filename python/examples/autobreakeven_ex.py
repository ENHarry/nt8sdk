from python.nt8.advanced_strategy import AdvancedStrategy, BreakevenConfig

config = BreakevenConfig(
    num_steps=3,                           # 1-3 steps
    profit_targets=[7.0, 10.0, 15.0],     # When to activate each step
    breakeven_offsets=[0.0, 2.0, 4.0],    # Where to place stop
    trailing_ticks=2,                      # Trail distance
    tick_size=0.25,                        # ES tick size
    enabled=True
)

strategy = AdvancedStrategy(
    instrument="ES 03-25",
    max_position=3,
    breakeven_config=config
)

strategy.run()