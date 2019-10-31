import requests
import time
for word in open("words").read().strip().split("\n"):
    print(f"\rTrying {word}              \r", end="")
    r = requests.get(f"http://halloween.kodsport.se/{word}?apiKey=cccd0fe7-22d4-4c76-93ec-e0aa52d32e52")
    if r.status_code != 404:
        print()
        print(f"Success: {word}")
        print()
