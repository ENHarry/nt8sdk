"""
Simple test to check the structure of positions returned from NT8
"""

import sys
import os

# Add the parent directory to Python path for imports
sys.path.append(os.path.join(os.path.dirname(__file__), '..'))

from nt8.client_filebased import NT8Client

def check_positions_structure():
    print("🔍 Checking positions structure...")
    
    client = NT8Client()
    
    try:
        # Test ping first
        response = client.ping()
        print(f"Ping response: {response}")
        
        # Get positions
        positions = client.get_positions()
        print(f"\nPositions type: {type(positions)}")
        print(f"Positions length: {len(positions) if positions else 0}")
        
        if positions:
            print(f"\nFirst position:")
            for i, pos in enumerate(positions[:2]):  # Just first 2
                print(f"  Position {i+1}: {type(pos)}")
                print(f"  Keys: {list(pos.keys()) if isinstance(pos, dict) else 'Not a dict'}")
                print(f"  Content: {pos}")
                print()
        else:
            print("No positions found")
            
    except Exception as e:
        print(f"❌ Error: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    check_positions_structure()